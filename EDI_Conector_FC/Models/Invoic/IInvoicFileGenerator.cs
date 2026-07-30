using EDI_Conector_FC.Models.ClientConfig;
using EDI_Conector_FC.Models.Invoic;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace EDI_Conector_FC.Services.Invoic
{
	public interface IInvoicFileGenerator
	{
		Task<string> GenerateAsync(InvoicFactura factura, ClientOptions client, CancellationToken ct);
	}

	public sealed class InvoicFileGenerator : IInvoicFileGenerator
	{
		private readonly ILogger<InvoicFileGenerator> _logger;

		public InvoicFileGenerator(ILogger<InvoicFileGenerator> logger)
		{
			_logger = logger;
		}

		public async Task<string> GenerateAsync(
			InvoicFactura factura, ClientOptions client, CancellationToken ct)
		{
			var invoic = client.Invoic!;
			var paths = client.Paths;

			Directory.CreateDirectory(paths.InvoicOutbox);

			var fechaHora = DateTime.Now.ToString("yyyyMMddHHmm");
			var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			var fileName = $"INVOIC_{factura.DocNum}_{stamp}.TXT";
			var filePath = Path.Combine(paths.InvoicOutbox, fileName);

			var glnRemitente = invoic.GlnRemitente;
			var glnCliente = factura.GlnCliente;
			var glnPuntoEntrega = factura.GlnPuntoEntrega;
			var vatIdComprador = invoic.VatIdComprador ?? "";

			// Importes formateados (18 chars, alineado derecha con 3 decimales)
			var importeNeto = FormatImporte(factura.Neto, 18);
			var baseImponible = FormatImporte(factura.Neto, 18);
			var importeBruto = FormatImporte(factura.Neto, 18);
			var importeTotal = FormatImporte(factura.Total, 18);

			var pedidoOrig = FormatPedidoOrig(factura.NumAtCard);
			var albaranOrig = Str(factura.AlbaranOrig, 8);

			var sb = new StringBuilder();

			// ── RECTL ─────────────────────────────────────────────────────
			sb.Append("RECTL INVOIC");
			sb.Append(Str(glnRemitente, 35));
			sb.Append(Str("", 35));
			sb.Append(Str(fechaHora, 40));
			sb.Append(fechaHora);
			sb.AppendLine();

			// ── SINCC ─────────────────────────────────────────────────────
			// Mapa exacto verificado caracter a caracter vs ECI:
			// 1-12  : "SINCC 380   "
			// 13-29 : DocNum (17)
			// 30-35 : spaces (6)
			// 36-47 : FechaFact (12)
			// 48-59 : spaces (12)
			// 60-61 : "60"
			// 62-71 : spaces (10)
			// 72-88 : PedidoOrig (17)
			// 89-96 : AlbaranOrig (8)
			// 97-159: spaces (63)
			// 160-162: "EUR"
			// 163-165: spaces (3)  → total EUR+spaces = 6 chars: "EUR   "
			// 166-177: Vencimiento (12)
			// 178-184: spaces (7)
			// 185-202: ImpNeto (18)
			// 203-220: BaseImponible (18)
			// 221-238: ImpBruto (18)
			// 239-256: ImpIVA (18)
			// 257-274: ImpTotal (18)
			// 275-370: spaces (96)
			sb.Append("SINCC 380   ");                      // 1-12
			sb.Append(Str(factura.DocNum, 17));             // 13-29
			sb.Append(Str("", 6));                          // 30-35
			sb.Append(Str(factura.DocDate, 12));            // 36-47
			sb.Append(Str("", 12));                         // 48-59
			sb.Append("60");                                // 60-61
			sb.Append(Str("", 10));                         // 62-71
			sb.Append(pedidoOrig);                          // 72-88 (17 chars)
			sb.Append(albaranOrig);                         // 89-96 (8 chars)
			sb.Append(Str("", 63));                         // 97-159
			sb.Append("EUR");                               // 160-162
			sb.Append(Str("", 3));                          // 163-165
			sb.Append(Str(factura.DocDueDate, 12));         // 166-177
			sb.Append(Str("", 7));                          // 178-184
			sb.Append(importeNeto);                         // 185-202
			sb.Append(baseImponible);                       // 203-220
			sb.Append(importeBruto);                        // 221-238
			sb.Append(FormatImporte(factura.VatSum, 18));   // 239-256 IVA
			sb.Append(importeTotal);                        // 257-274
			sb.Append(Str("", 96));                         // 275-370
			sb.AppendLine();

			// ── SINCP SU — suministrador ──────────────────────────────────
			sb.Append("SINCP SU ");
			sb.Append(Str(glnRemitente, 13));
			sb.Append("    9  ");
			sb.Append(Str("", 744));
			sb.AppendLine();

			// ── SINCP II — emisor factura ─────────────────────────────────
			sb.Append("SINCP II ");
			sb.Append(Str(glnRemitente, 13));
			sb.Append("    9  ");
			sb.Append(Str("", 744));
			sb.AppendLine();

			// ── SINCP DP — punto de entrega ───────────────────────────────
			sb.Append("SINCP DP ");
			sb.Append(Str(glnPuntoEntrega, 17));
			sb.Append("9  ");
			sb.Append(Str("", 744));
			sb.AppendLine();

			// ── SINCP BY — comprador + VAT ID Boozt ──────────────────────
			// Campo 18 (Número Identificación Fiscal) en posición 401, longitud 35
			// Estructura: "SINCP BY " + GLN(17) + "9  " + spaces hasta pos 401 + VatId(35) + spaces
			// Pos 1-9  : "SINCP BY "
			// Pos 10-26: GLN (17)
			// Pos 27-29: "9  "
			// Pos 30-400: spaces (371)
			// Pos 401-435: VatId (35)
			// Pos 436-773: spaces (338)
			sb.Append("SINCP BY ");
			sb.Append(Str(glnCliente, 17));
			sb.Append("9  ");
			sb.Append(Str("", 371));                        // hasta pos 401
			sb.Append(Str(vatIdComprador, 35));                 // VAT ID comprador (vacío si no configurado)
			sb.Append(Str("", 338));
			sb.AppendLine();

			// ── SINCP IV — facturado a ────────────────────────────────────
			sb.Append("SINCP IV ");
			sb.Append(Str(glnCliente, 13));
			sb.Append("    9  ");
			sb.Append(Str("", 744));
			sb.AppendLine();

			// ── SINCP MS — remitente ──────────────────────────────────────
			sb.Append("SINCP MS ");
			sb.Append(Str(glnRemitente, 13));
			sb.Append("    9  ");
			sb.Append(Str("", 744));
			sb.AppendLine();

			// ── SINCP MR — destinatario ───────────────────────────────────
			sb.Append("SINCP MR ");
			sb.Append(Str(glnCliente, 13));
			sb.Append("    9  ");
			sb.Append(Str("", 744));
			sb.AppendLine();

			// ── SINCL — líneas de artículo ────────────────────────────────
			for (int i = 0; i < factura.Lineas.Count; i++)
			{
				var ln = factura.Lineas[i];

				sb.Append("SINCL ");
				sb.Append(NLinea(i));
				sb.Append(" ");
				sb.Append(Str(ln.Ean, 14));
				sb.Append(Str(ln.Descripcion, 35));
				sb.Append("M");
				sb.Append(Str("", 75));
				sb.Append(NumR(FormatCantidad(ln.Quantity), 16));
				sb.Append(Str("", 54));
				sb.Append(NumR(FormatImporteLinea(ln.TotalLinea), 18));
				sb.Append(NumR(FormatPrecio(ln.Price), 16));
				sb.Append(NumR(FormatPrecio(ln.Price), 16));
				sb.Append(Str("", 6));
				sb.Append("EXT         ");  // Boozt = exportación
				sb.Append(Str("", 263));
				sb.AppendLine();
			}

			// ── SINCI — resumen impuestos ─────────────────────────────────
			// Formato exacto ECI: "SINCI  7EXT    00.00             0.000           181.800"
			// pos 1-7  : "SINCI  "
			// pos 8    : num línea ("1")
			// pos 9-14 : calificador (6): "EXT   " o "VAT   "
			// pos 15-20: " " + % IVA (5 chars): " 00.00" o " 21.00"
			// pos 21-38: ImpIVA (18, alineado derecha)
			// pos 39-56: BaseImponible (18, alineado izquierda como SINCC)
			var tieneIva = factura.VatSum > 0;
			var calificador = tieneIva ? "VAT   " : "EXT   ";
			var pctIva = tieneIva && factura.Neto > 0
				? " " + (factura.VatSum / factura.Neto * 100m).ToString("00.00", CultureInfo.InvariantCulture)
				: " 00.00";

			sb.Append("SINCI  ");                                        // pos 1-7
			sb.Append("1");                                              // pos 8
			sb.Append(calificador);                                      // pos 9-14
			sb.Append(pctIva);                                           // pos 15-20
			sb.Append(FormatImporteRight(factura.VatSum, 18));           // pos 21-38 (derecha)
			sb.Append(FormatImporteRight(factura.Neto, 18));             // pos 39-56 (derecha)
			sb.AppendLine();

			await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.Latin1, ct);

			_logger.LogInformation(
				"INVOIC generado: {File} — {Count} línea(s) — Total={Total}",
				fileName, factura.Lineas.Count, factura.Total);

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

		private static string FormatImporte(decimal value, int largo)
		{
			var s = value.ToString("0.000", CultureInfo.InvariantCulture);
			// Alineado a la izquierda con spaces a la derecha (igual que ECI en SINCC)
			var padded = s.PadRight(largo);
			return padded.Length > largo ? padded[..largo] : padded;
		}

		/// <summary>Importe alineado a la derecha — usado en SINCI.</summary>
		private static string FormatImporteRight(decimal value, int largo)
		{
			var s = value.ToString("0.000", CultureInfo.InvariantCulture);
			var padded = s.PadLeft(largo);
			return padded.Length > largo ? padded[^largo..] : padded;
		}

		private static string FormatImporteLinea(decimal value)
			=> value.ToString("0.000", CultureInfo.InvariantCulture);

		private static string FormatPrecio(decimal value)
			=> value.ToString("0.000", CultureInfo.InvariantCulture);

		private static string FormatCantidad(decimal qty)
			=> qty.ToString("0.######", CultureInfo.InvariantCulture);

		private static string FormatPedidoOrig(string? origen)
		{
			if (string.IsNullOrWhiteSpace(origen)) return Str("", 17);
			if (origen.Length < 6) return Str("000" + origen, 17);
			if (origen.Length == 6) return Str("00" + origen, 17);
			if (origen.Length == 7) return Str("0" + origen, 17);
			return Str(origen, 17);
		}
	}
}