using EDI_Conector_FC.Models.ClientConfig;
using EDI_Conector_FC.Models.Desadv;
using Microsoft.Extensions.Logging;
using System.Text;

namespace EDI_Conector_FC.Services.Desadv
{
	public interface IDesadvFileGenerator
	{
		Task<string> GenerateAsync(DesadvAlbaran albaran, ClientOptions client, CancellationToken ct);
	}

	public sealed class DesadvFileGenerator : IDesadvFileGenerator
	{
		private readonly ILogger<DesadvFileGenerator> _logger;

		public DesadvFileGenerator(ILogger<DesadvFileGenerator> logger)
		{
			_logger = logger;
		}

		public async Task<string> GenerateAsync(
			DesadvAlbaran albaran, ClientOptions client, CancellationToken ct)
		{
			var desadv = client.Desadv;
			var paths = client.Paths;

			Directory.CreateDirectory(paths.DesadvOutbox);

			var fechaHora = DateTime.Now.ToString("yyyyMMddHHmm");
			var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			var fileName = $"DESADV_{albaran.DocNum}_{stamp}.TXT";
			var filePath = Path.Combine(paths.DesadvOutbox, fileName);

			var sb = new StringBuilder();

			// ── RECTL ─────────────────────────────────────────────────────
			sb.Append("RECTL DESADV");
			sb.Append(Str(desadv.GlnRemitente, 35));
			sb.Append(Str("", 35));
			sb.Append(Str(fechaHora, 40));
			sb.Append(fechaHora);
			sb.AppendLine();

			var pedidoOrig = FormatPedidoOrigen(albaran.NumAtCard);

			// ── SEH1C ─────────────────────────────────────────────────────
			// Mapa exacto basado en ECI:
			// 1-12  : "SEH1C 351   " (12 chars incl. 2 spaces al final)
			// 13-19 : DocNum (variable, relleno dcho con spaces hasta pos 29)
			// 30    : "9"
			// 31-35 : 5 spaces
			// 36-47 : FechaHora (12)
			// 48-59 : FechaEntrega (12)
			// 60-94 : 35 spaces
			// 95-111: PedidoOrig (17)
			// pos 113-124 spaces
			// 124-133: DocNum (10 chars)
			// 134-145: FechaHora (12)
			// 146-448: spaces
			var docNumField = albaran.DocNum.PadRight(17); // DocNum + spaces hasta pos 29 (17 chars desde pos 13)
			sb.Append("SEH1C 351   ");               // pos 1-12
			sb.Append(docNumField);                  // pos 13-29 (DocNum relleno hasta pos 29)
			sb.Append("9");                          // pos 30
			sb.Append(Str("", 5));                   // pos 31-35
			sb.Append(Str(fechaHora, 12));           // pos 36-47
			sb.Append(Str(albaran.DocDueDate, 12));  // pos 48-59
			sb.Append(Str("", 36));                  // pos 60-95 (36 spaces)
			sb.Append(Str(pedidoOrig, 17));          // pos 96-112
			sb.Append(Str("", 12));                   // pos 113-124 spaces
			sb.Append(Str(albaran.DocNum, 10));      // pos 125-134
			sb.Append(Str("", 7));                   // pos 135-141 spaces
			sb.Append(Str(fechaHora, 12));           // pos 142-153
			sb.Append(Str("VN", 3));                 // pos 154-156 calificador
			sb.Append(Str(albaran.DocNumPedido, 17));// pos 157-173 DocNum pedido SAP
			sb.Append(Str(fechaHora, 12));           // pos 174-185 fecha/hora referencia
			sb.Append(Str("", 263));                 // pos 186-448 resto
			sb.AppendLine();

			// ── SEH1D MS — remitente ──────────────────────────────────────
			sb.Append("SEH1D MS ");
			sb.Append(Str(desadv.GlnRemitente, 13));
			sb.Append("    9  ");
			sb.Append(Str("", 484));
			sb.AppendLine();

			// ── SEH1D MR — destinatario (GLN cliente BY/IV) ───────────────
			sb.Append("SEH1D MR ");
			sb.Append(Str(albaran.GlnCliente, 13));
			sb.Append("    9  ");
			sb.Append(Str("", 484));
			sb.AppendLine();

			// ── SEH1D SU — suministrador ──────────────────────────────────
			sb.Append("SEH1D SU ");
			sb.Append(Str(desadv.GlnRemitente, 13));
			sb.Append("    9  ");
			sb.Append(Str("", 484));
			sb.AppendLine();

			// ── SEH1D DP — punto de entrega ───────────────────────────────
			sb.Append("SEH1D DP ");
			sb.Append(Str(albaran.GlnPuntoEntrega, 13));
			sb.Append("    9  ");
			sb.Append(Str("", 484));
			sb.AppendLine();

			// ── SEH1D BY — comprador (GLN fijo del BP) ────────────────────
			sb.Append("SEH1D BY ");
			sb.Append(Str(albaran.GlnCliente, 13));
			sb.Append("    9  ");
			sb.Append(Str("", 484));
			sb.AppendLine();

			// ── SEH1P 1 — paquete raíz ────────────────────────────────────
			sb.Append("SEH1P ");
			sb.Append(Str("1", 12));
			sb.Append(Str("", 12));
			sb.Append(NumR("1", 8));
			sb.Append("            201   ");
			sb.Append(Str("", 769));
			sb.AppendLine();

			// ── SEH1P 2 — bulto raíz ──────────────────────────────────────
			sb.Append("SEH1P ");
			sb.Append(Str("2", 12));
			sb.Append(Str("1", 12));
			sb.Append(NumR("1", 8));
			sb.Append("            BE    ");
			sb.Append(Str("", 769));
			sb.AppendLine();

			// ── SEH1P + SEH1L por cada línea/bulto ───────────────────────
			// linea.Bulto es el número real de bulto (U_SEI_NumBulto, puede ser grande);
			// se usa para el SSCC. "posicion" es un contador chico y secuencial, propio de
			// la numeración interna de segmentos SEH1P del fichero (no tiene relación con
			// el número de bulto real).
			int bultoActual = int.MinValue;
			int posicion = 0;

			for (int i = 0; i < albaran.Lineas.Count; i++)
			{
				var linea = albaran.Lineas[i];

				if (linea.Bulto != bultoActual)
				{
					bultoActual = linea.Bulto;
					posicion++;
					var sscc = GenerarSscc(albaran.DocNum, bultoActual);

					sb.Append("SEH1P ");
					sb.Append(Str((posicion + 2).ToString(), 12));
					sb.Append(Str("2", 12));
					sb.Append(NumR("1", 8));
					sb.Append("            CT    ");
					sb.Append(Str("", 561));
					sb.Append(Str(sscc, 35));
					sb.Append(Str("", 175));
					sb.AppendLine();
				}

				sb.Append("SEH1L ");
				sb.Append(NLinea(i));
				sb.Append(Str(linea.Ean, 15));
				sb.Append(Str(linea.Descripcion, 70));
				sb.Append("CU     ");
				sb.Append(Str("", 95));
				sb.Append(Str(linea.ItemCode, 15));
				sb.Append(NumR(FormatCantidad(linea.Cantidad), 16));
				sb.Append(Str("", 394));
				sb.AppendLine();
			}

			await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.Latin1, ct);

			_logger.LogInformation(
				"DESADV generado: {File} — {Count} línea(s)",
				fileName, albaran.Lineas.Count);

			return filePath;
		}

		// ── Helpers ───────────────────────────────────────────────────────

		private static string Str(string? s, int t)
		{
			s ??= "";
			if (s.Length > t) s = s[..t];
			return s.PadRight(t);
		}

		private static string NumR(string? s, int t)
		{
			s ??= "";
			return s.PadLeft(t);
		}

		private static string NLinea(int idx)
			=> (idx + 1).ToString().PadLeft(6);

		/// <summary>
		/// Arma el SSCC (18 dígitos: prefijo de 9 + 8 dígitos de referencia serial + dígito
		/// de control). El bulto real (U_SEI_NumBulto) puede tener varios dígitos, así que se
		/// usa completo y se recorta cuánto se toma del DocNum para que el total siga dando 17
		/// dígitos antes del dígito de control. Si el bulto ocupara más de 8 dígitos (caso
		/// extremo), se toman sus últimos 8.
		/// </summary>
		private static string GenerarSscc(string docNum, int bulto)
		{
			const string prefijo = "084357207"; // 9 dígitos
			const int espacioSerial = 8; // 17 - 9

			string bultoPart = Math.Abs(bulto).ToString(System.Globalization.CultureInfo.InvariantCulture);
			if (bultoPart.Length > espacioSerial) bultoPart = bultoPart[^espacioSerial..];

			int numLen = espacioSerial - bultoPart.Length;
			string numPart = numLen <= 0
				? ""
				: (docNum.Length >= numLen ? docNum[^numLen..] : docNum.PadLeft(numLen, '0'));

			string codigo = prefijo + numPart + bultoPart;
			return codigo + CalcularDigitoControl(codigo);
		}

		private static int CalcularDigitoControl(string codigo)
		{
			int suma = 0, longitud = codigo.Length;
			for (int i = 0; i < longitud; i++)
			{
				int num = codigo[i] - '0';
				suma += (longitud - i) % 2 == 0 ? num : num * 3;
			}
			return (10 - suma % 10) % 10;
		}

		private static string FormatCantidad(decimal qty)
			=> qty.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

		private static string FormatPedidoOrigen(string? origen)
		{
			if (string.IsNullOrWhiteSpace(origen)) return "";
			return origen.Length < 8 ? "0" + origen : origen;
		}
	}
}