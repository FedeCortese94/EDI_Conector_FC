using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace EDI_Conector_FC.Services.OrdersIn
{
	public sealed record EdiOrder(
		string EdiDocNum,
		DateOnly DocDate,
		DateOnly DocDueDate,
		string? Season,
		string? EciDept,
		List<EdiOrderLine> Lines
	);

	public sealed record EdiOrderLine(
		int LineNo,
		string Ean,
		decimal Quantity,
		decimal? QtyAlt
	);

	public interface IEre1OrdersParser
	{
		EdiOrder ParseFile(string filePath);
	}

	public sealed class Ere1OrdersParser : IEre1OrdersParser
	{
		public EdiOrder ParseFile(string filePath)
		{
			var lines = File.ReadAllLines(filePath);

			string? ediDocNum = null;
			DateOnly? docDate = null;
			DateOnly? docDueDate = null;
			string? season = null;
			string? eciDept = null;

			var orderLines = new List<EdiOrderLine>();
			int lineNo = 0;

			foreach (var raw in lines)
			{
				if (raw.StartsWith("ERE1C"))
				{
					ediDocNum = SafeSlice(raw, 9, 16).Trim().Split(' ')[0];

					var dates = Regex.Matches(raw, @"202\d{5}")
									 .Select(m => m.Value)
									 .ToList();

					if (dates.Count >= 1) docDate = ParseDate(dates[0]);
					if (dates.Count >= 2) docDueDate = ParseDate(dates[1]);

					var deptMatch = Regex.Match(raw, @"\s(\d{4})\s");
					if (deptMatch.Success) eciDept = deptMatch.Groups[1].Value;

					if (raw.Contains("OTONNO-INVIERNO")) season = "OTONNO-INVIERNO";
					else if (raw.Contains("PRIMAVERA-VERANO")) season = "PRIMAVERA-VERANO";
					else
					{
						var sMatch = Regex.Match(raw, @"[A-Z]{2}(OTONNO-INVIERNO|PRIMAVERA-VERANO)");
						if (sMatch.Success) season = sMatch.Groups[1].Value;
					}
				}
				else if (raw.StartsWith("ERE1L"))
				{
					// ✅ EAN bueno: posición 13..26 (13 chars), contando desde 0
					// Ejemplo: "0198410202067"
					if (raw.Length < 27)
						throw new Exception($"Línea ERE1L demasiado corta para extraer EAN (pos 13..26): {raw}");


					var eanRaw = raw.Substring(13, 13).Trim();


					if (eanRaw.Length < 11 || eanRaw.Length > 13 || !eanRaw.All(char.IsDigit))
						throw new Exception($"EAN inválido en ERE1L (pos 13..26): '{eanRaw}' Línea: {raw}");

					// Si viene 12, lo dejamos tal cual; si viene 13 con 0 delante, también.
					var ean = eanRaw;


					// Cantidades (se mantienen como estaban)
					var qty1 = ParseDecimalFixed(raw, 328, 17); // "000000000002.000"
					var qty2 = ParseDecimalFixed(raw, 366, 17); // "000000000001.000" (si existe)

					orderLines.Add(new EdiOrderLine(
						LineNo: lineNo++,
						Ean: ean,
						Quantity: qty1,
						QtyAlt: qty2
					));
				}
			}

			if (ediDocNum is null || docDate is null || docDueDate is null)
				throw new Exception($"Faltan datos de cabecera ERE1C en {filePath}");

			return new EdiOrder(
				EdiDocNum: ediDocNum,
				DocDate: docDate.Value,
				DocDueDate: docDueDate.Value,
				Season: season,
				EciDept: eciDept,
				Lines: orderLines
			);
		}

		private static string SafeSlice(string s, int start, int len)
			=> s.Length >= start + len ? s.Substring(start, len) : s.Substring(Math.Min(start, s.Length));

		private static DateOnly ParseDate(string yyyymmdd)
			=> DateOnly.ParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture);

		private static decimal ParseDecimalFixed(string s, int start, int len)
		{
			var part = SafeSlice(s, start, len).Trim();
			if (string.IsNullOrWhiteSpace(part)) return 0m;
			return decimal.Parse(part, CultureInfo.InvariantCulture);
		}
	}
}