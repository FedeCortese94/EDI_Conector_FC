using EDI_Conector_FC.Models.ClientConfig;
using EDI_Conector_FC.Models.OrdRsp;
using Microsoft.Extensions.Logging;
using System.Text;

namespace EDI_Conector_FC.Services.OrdRsp
{
	public interface IOrdRspFileGenerator
	{
		Task<string> GenerateAsync(OrdRspPedido pedido, ClientOptions client, CancellationToken ct);
	}

	/// <summary>
	/// Genera el fichero ORDRSP (confirmación de pedido) reusando la misma disposición de
	/// posiciones que ya conocemos del ORDER de entrada (RECTL/ERE1C/ERE1P/ERE1L), medida
	/// byte a byte contra un fichero real de Nexmart. El campo de precio no existe en el
	/// ORDER de entrada (nunca lo trae) — se agregó como campo nuevo al final de cada ERE1L.
	/// Posición pendiente de confirmar con Nexmart si hiciera falta ajustarla.
	/// </summary>
	public sealed class OrdRspFileGenerator : IOrdRspFileGenerator
	{
		private readonly ILogger<OrdRspFileGenerator> _logger;

		public OrdRspFileGenerator(ILogger<OrdRspFileGenerator> logger)
		{
			_logger = logger;
		}

		public async Task<string> GenerateAsync(
			OrdRspPedido pedido, ClientOptions client, CancellationToken ct)
		{
			var ordrsp = client.OrdRsp!;
			var paths = client.Paths;

			Directory.CreateDirectory(paths.OrdRspOutbox);

			var fechaHora = DateTime.Now.ToString("yyyyMMddHHmm");
			var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			var fileName = $"ORDRSP_{pedido.DocNum}_{stamp}.TXT";
			var filePath = Path.Combine(paths.OrdRspOutbox, fileName);

			var sb = new StringBuilder();

			// ── RECTL ─────────────────────────────────────────────────────
			// Mismo layout que el RECTL ORDERS de entrada (medido byte a byte), con los
			// roles de remitente/destinatario invertidos: ahora nosotros somos el emisor.
			// 1-12  : "RECTL ORDRSP"
			// 13-25 : GLN remitente (13, nuestro GLN)
			// 26-47 : spaces (22)
			// 48-60 : GLN destinatario (13, GLN del cliente)
			// 61-82 : spaces (22)
			// 83-92 : NumAtCard / pedido origen (10)
			// 93-122: spaces (30)
			// 123-134: FechaHora (12)
			sb.Append("RECTL ORDRSP");
			sb.Append(Str(ordrsp.GlnRemitente, 13));
			sb.Append(Str("", 22));
			sb.Append(Str(pedido.GlnCliente, 13));
			sb.Append(Str("", 22));
			sb.Append(Str(pedido.NumAtCard, 10));
			sb.Append(Str("", 30));
			sb.Append(fechaHora);
			sb.AppendLine();

			// ── ERE1C (cabecera) ──────────────────────────────────────────
			// Mismo layout medido byte a byte del ERE1C de entrada (posiciones 0-indexadas,
			// igual que usa el parser Ere1OrdersParser):
			// [0,6)  : "ERE1C "
			// [6,9)  : TipoDoc (3) — "220" fijo, es una confirmación
			// [9,25) : NumAtCard (16)
			// [25]   : espacio
			// [26]   : "9"
			// [27,29): espacios (2)
			// [29,37): DocDate (8)
			// [37,41): espacios (4)
			// [41]   : "2"
			// [42,44): espacios (2)
			// [44,52): DocDueDate (8)
			// [52,56): espacios (4)
			// [56,58): "63"
			// [58]   : espacio
			// [59,67): DocDueDate repetida (8) — igual que en el ORDER de entrada
			// resto  : espacios hasta completar el largo observado (287)
			sb.Append("ERE1C ");                    // [0,6)
			sb.Append("220");                       // [6,9)
			sb.Append(Str(pedido.NumAtCard, 16));   // [9,25)
			sb.Append(" ");                         // [25]
			sb.Append("9");                         // [26]
			sb.Append(Str("", 2));                  // [27,29)
			sb.Append(Str(pedido.DocDate, 8));      // [29,37)
			sb.Append(Str("", 4));                  // [37,41)
			sb.Append("2");                         // [41]
			sb.Append(Str("", 2));                  // [42,44)
			sb.Append(Str(pedido.DocDueDate, 8));   // [44,52)
			sb.Append(Str("", 4));                  // [52,56)
			sb.Append("63");                        // [56,58)
			sb.Append(" ");                         // [58]
			sb.Append(Str(pedido.DocDueDate, 8));   // [59,67)
			sb.Append(Str("", 220));                // relleno hasta el largo total observado
			sb.AppendLine();

			// ── ERE1P DP — punto de entrega (mismo layout: GLN en pos 9, 13 chars) ──
			sb.Append("ERE1P DP ");
			sb.Append(Str(pedido.GlnPuntoEntrega, 13));
			sb.Append("    9  ");
			sb.Append(Str("", 100));
			sb.AppendLine();

			// ── ERE1L por línea — mismo layout medido del ORDER de entrada:
			// [0,12) : "ERE1L " + número de línea (6)
			// [12,25): EAN (13)
			// [25,328): relleno (303) — observado vacío en el ORDER real
			// [328,345): Cantidad (17) — "000000000000.000" + 1 espacio, zero-padded
			// [345,366): relleno (21)
			// [366,383): QtyAlt (17) — 16 ceros + 1 espacio, siempre así en el ORDER real
			// [383,...): NUEVO — Precio confirmado (18, zero-padded, 3 decimales).
			//            No existe en el ORDER de entrada (nunca trae precio); posición a
			//            confirmar con Nexmart si hiciera falta ajustarla.
			foreach (var linea in pedido.Lineas)
			{
				sb.Append("ERE1L ");
				sb.Append(NLinea(linea.LineNum));
				sb.Append(Str(linea.Ean, 13));
				sb.Append(Str("", 303));
				sb.Append(FormatCantidadFija(linea.Cantidad, 12));
				sb.Append(Str("", 21));
				sb.Append(new string('0', 16));
				sb.Append(" ");
				sb.Append(FormatPrecioFijo(linea.Precio));
				sb.AppendLine();
			}

			await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.Latin1, ct);

			_logger.LogInformation(
				"ORDRSP generado: {File} — {Count} línea(s)",
				fileName, pedido.Lineas.Count);

			return filePath;
		}

		// ── Helpers ───────────────────────────────────────────────────────

		private static string Str(string? s, int t)
		{
			s ??= "";
			if (s.Length > t) s = s[..t];
			return s.PadRight(t);
		}

		private static string NLinea(int idx)
			=> (idx + 1).ToString().PadLeft(6);

		/// <summary>Zero-padded "NNN...N.000" + 1 espacio final = (dígitosEnteros + 4 + 1) chars totales.</summary>
		private static string FormatCantidadFija(decimal valor, int digitosEnteros)
		{
			var s = valor.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
			var partes = s.Split('.');
			var entero = partes[0].PadLeft(digitosEnteros, '0');
			return entero + "." + partes[1] + " ";
		}

		/// <summary>Precio nuevo (no existe en el ORDER de entrada): 14 dígitos enteros + "." + 3 decimales, zero-padded, 18 chars.</summary>
		private static string FormatPrecioFijo(decimal precio)
		{
			var s = precio.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
			var partes = s.Split('.');
			var entero = partes[0].PadLeft(14, '0');
			return entero + "." + partes[1];
		}
	}
}
