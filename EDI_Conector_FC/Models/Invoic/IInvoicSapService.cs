using EDI_Conector_FC.Models.ClientConfig;
using EDI_Conector_FC.Models.Desadv;
using EDI_Conector_FC.Models.Invoic;
using EDI_Conector_FC.Services.ServiceSAP.Http;
using Microsoft.Extensions.Logging;

namespace EDI_Conector_FC.Services.Invoic
{
	public interface IInvoicSapService
	{
		Task<List<InvoicFactura>> GetPendingFacturasAsync(ClientOptions client, CancellationToken ct);
		Task MarkAsSentAsync(int docEntry, CancellationToken ct);
	}

	public sealed class InvoicSapService : IInvoicSapService
	{
		private readonly ServiceLayerClient _sl;
		private readonly ILogger<InvoicSapService> _logger;

		public InvoicSapService(ServiceLayerClient sl, ILogger<InvoicSapService> logger)
		{
			_sl = sl;
			_logger = logger;
		}

		public async Task<List<InvoicFactura>> GetPendingFacturasAsync(
			ClientOptions client, CancellationToken ct)
		{
			var cardCode = client.SapDefaults.CardCode;
			var result = new List<InvoicFactura>();

			var filter = Uri.EscapeDataString(
				$"Cancelled eq 'tNO' and CardCode eq '{cardCode}' and " +
				$"(U_SEI_ENVIOMAILF eq '' or U_SEI_ENVIOMAILF eq null or U_SEI_ENVIOMAILF eq '3')");

			var select = "DocEntry,DocNum,NumAtCard,DocDate,DocDueDate,CardCode,DocTotal,VatSum,U_SEI_PO_EDI,DocumentLines";
			var url = $"Invoices?$filter={filter}&$select={select}";

			var response = await _sl.GetAsync<SlInvoicListResponse>(url, ct);

			if (response?.Value == null || response.Value.Count == 0)
			{
				_logger.LogInformation("INVOIC: no hay facturas pendientes para {CardCode}", cardCode);
				return result;
			}

			_logger.LogInformation("INVOIC: {Count} factura(s) pendiente(s) para {CardCode}",
				response.Value.Count, cardCode);

			// GLN del cliente (BY e IV — fijo del BP)
			var glnCliente = await GetGlnClienteAsync(cardCode, ct);

			if (string.IsNullOrWhiteSpace(glnCliente))
			{
				glnCliente = client.Invoic?.GlnDestinatarioFallback ?? "";
				_logger.LogWarning("INVOIC: {CardCode} sin U_SEIPOEDI. Usando fallback={Gln}",
					cardCode, glnCliente);
			}

			foreach (var inv in response.Value)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					var factura = await BuildFacturaAsync(inv, glnCliente, ct);
					result.Add(factura);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "INVOIC: error procesando factura DocNum={DocNum}", inv.DocNum);
				}
			}

			return result;
		}

		public async Task MarkAsSentAsync(int docEntry, CancellationToken ct)
		{
			await _sl.PatchAsync($"Invoices({docEntry})", new { U_SEI_ENVIOMAILF = "1" });
			_logger.LogInformation("INVOIC: factura DocEntry={DocEntry} marcada como enviada.", docEntry);
		}

		// ─────────────────────────────────────────────────────────────────

		private async Task<string?> GetGlnClienteAsync(string cardCode, CancellationToken ct)
		{
			try
			{
				var escaped = cardCode.Replace("'", "''");
				var bp = await _sl.GetAsync<SlBusinessPartner>(
					$"BusinessPartners('{escaped}')?$select=U_SEIPOEDI", ct);
				return bp?.U_SEIPOEDI?.Trim();
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "INVOIC: error leyendo U_SEIPOEDI del cliente {CardCode}", cardCode);
				return null;
			}
		}

		/// <summary>
		/// GLN punto de entrega en cascada:
		/// 1. U_SEI_PO_EDI de la factura
		/// 2. U_SEI_PO_EDI del albarán origen (BaseEntry de la primera línea)
		/// 3. U_SEI_PO_EDI del pedido origen
		/// 4. GLN del cliente como fallback
		/// </summary>
		private async Task<string?> GetGlnPuntoEntregaAsync(
			SlInvoice inv, string glnClienteFallback, CancellationToken ct)
		{
			// 1. De la factura directamente
			if (!string.IsNullOrWhiteSpace(inv.U_SEI_PO_EDI))
				return inv.U_SEI_PO_EDI.Trim();

			// 2. Del albarán origen
			var baseEntry = inv.DocumentLines?.FirstOrDefault()?.BaseEntry;
			if (baseEntry.HasValue && baseEntry.Value > 0)
			{
				try
				{
					var dn = await _sl.GetAsync<SlDeliveryNoteGln>(
						$"DeliveryNotes({baseEntry.Value})?$select=U_SEI_PO_EDI,DocumentLines", ct);

					if (!string.IsNullOrWhiteSpace(dn?.U_SEI_PO_EDI))
						return dn.U_SEI_PO_EDI.Trim();

					// 3. Del pedido origen (BaseEntry del albarán)
					var baseEntryOrder = dn?.DocumentLines?.FirstOrDefault()?.BaseEntry;
					if (baseEntryOrder.HasValue && baseEntryOrder.Value > 0)
					{
						var order = await _sl.GetAsync<SlOrderGln>(
							$"Orders({baseEntryOrder.Value})?$select=U_SEI_PO_EDI", ct);
						if (!string.IsNullOrWhiteSpace(order?.U_SEI_PO_EDI))
							return order.U_SEI_PO_EDI.Trim();
					}
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "INVOIC: no se pudo leer GLN punto entrega del albarán origen {BaseEntry}", baseEntry);
				}
			}

			// 4. Fallback
			_logger.LogWarning("INVOIC: factura {DocNum} sin GLN punto entrega — usando GLN cliente.", inv.DocNum);
			return glnClienteFallback;
		}

		private async Task<InvoicFactura> BuildFacturaAsync(
			SlInvoice inv, string glnCliente, CancellationToken ct)
		{
			var albaranOrig = inv.DocumentLines
				.Where(l => l.BaseDocNum.HasValue && l.BaseDocNum.Value > 0)
				.Select(l => l.BaseDocNum!.Value.ToString())
				.FirstOrDefault() ?? "";

			var glnPuntoEntrega = await GetGlnPuntoEntregaAsync(inv, glnCliente, ct);

			var factura = new InvoicFactura
			{
				DocEntry = inv.DocEntry,
				DocNum = inv.DocNum.ToString(),
				NumAtCard = inv.NumAtCard?.Trim() ?? "",
				AlbaranOrig = albaranOrig,
				DocDate = FormatDate(inv.DocDate),
				DocDueDate = FormatDate(inv.DocDueDate),
				GlnCliente = glnCliente,
				GlnPuntoEntrega = glnPuntoEntrega ?? glnCliente,
				DocTotal = inv.DocTotal,
				VatSum = inv.VatSum,
			};

			int lineIdx = 0;
			foreach (var ln in inv.DocumentLines)
			{
				factura.Lineas.Add(new InvoicLinea
				{
					LineNum = lineIdx++,
					ItemCode = ln.ItemCode ?? "",
					Ean = ln.BarCode?.Trim() ?? "",
					Descripcion = NormalizeText(ln.ItemDescription ?? ""),
					Quantity = ln.Quantity,
					Price = ln.Price,
				});
			}

			_logger.LogInformation(
				"INVOIC: factura {DocNum} — {Count} línea(s) — Total={Total} GLN_BY={Gln} GLN_DP={Dp}",
				factura.DocNum, factura.Lineas.Count, factura.Total, glnCliente, factura.GlnPuntoEntrega);

			return factura;
		}

		private static string FormatDate(string? sapDate)
		{
			if (string.IsNullOrWhiteSpace(sapDate)) return "";
			// SAP puede devolver "2026-09-07T00:00:00Z" o "20260907" — siempre normalizar a yyyyMMdd
			if (DateTime.TryParse(sapDate, null,
					System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
				return dt.ToString("yyyyMMdd");
			// Si ya viene en formato yyyyMMdd, devolverlo tal cual (máx 8 chars)
			return sapDate.Length > 8 ? sapDate[..8] : sapDate;
		}

		private static string NormalizeText(string text)
		{
			var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
			var sb = new System.Text.StringBuilder();
			foreach (var c in normalized)
			{
				var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
				if (cat != System.Globalization.UnicodeCategory.NonSpacingMark)
					sb.Append(c);
			}
			return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
		}

		// DTOs internos para las consultas de GLN
		private sealed class SlDeliveryNoteGln
		{
			public string? U_SEI_PO_EDI { get; set; }
			public List<SlDnLineGln> DocumentLines { get; set; } = new();
		}
		private sealed class SlDnLineGln { public int? BaseEntry { get; set; } }
		private sealed class SlOrderGln { public string? U_SEI_PO_EDI { get; set; } }
	}
}