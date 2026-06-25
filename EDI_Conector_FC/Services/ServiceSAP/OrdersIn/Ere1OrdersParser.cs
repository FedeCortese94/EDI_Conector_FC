using System.Globalization;
using System.Text.RegularExpressions;

namespace EDI_Conector_FC.Services.OrdersIn
{
    public sealed record EdiOrder(
        string EdiDocNum,
        string TipoDoc,          // "220" = Inicial, "224" = Repetición
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
            string tipoDoc = "220"; // por defecto Inicial
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
                    // Tipo documento: posición 6, longitud 3 (220, 224, etc.)
                    tipoDoc = SafeSlice(raw, 6, 3).Trim();

                    // Número de pedido: posición 9, hasta el primer espacio
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
                    if (raw.Length < 27)
                        throw new Exception($"Línea ERE1L demasiado corta: {raw}");

                    var eanRaw = raw.Substring(12, 13).Trim();

                    if (eanRaw.Length < 11 || eanRaw.Length > 13 || !eanRaw.All(char.IsDigit))
                        throw new Exception($"EAN inválido en ERE1L: '{eanRaw}' Línea: {raw}");

                    var qty1 = ParseDecimalFixed(raw, 328, 17);
                    var qty2 = ParseDecimalFixed(raw, 366, 17);

                    orderLines.Add(new EdiOrderLine(
                        LineNo: lineNo++,
                        Ean: eanRaw,
                        Quantity: qty1,
                        QtyAlt: qty2
                    ));
                }
            }

            if (ediDocNum is null || docDate is null || docDueDate is null)
                throw new Exception($"Faltan datos de cabecera ERE1C en {filePath}");

            return new EdiOrder(
                EdiDocNum: ediDocNum,
                TipoDoc: tipoDoc,
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