using EDI_Conector_FC.Models.ClientConfig;
using EDI_Conector_FC.Models.Desadv;
using EDI_Conector_FC.Services.ServiceSAP;
using EDI_Conector_FC.Services.ServiceSAP.Http;
using Microsoft.Extensions.Logging;

namespace EDI_Conector_FC.Services.Desadv
{
	public interface IDesadvSapService
	{
		Task<List<DesadvAlbaran>> GetPendingAlbaranesAsync(ClientOptions client, CancellationToken ct);
		Task MarkAsSentAsync(int docEntry, CancellationToken ct);
	}

	public sealed class DesadvSapService : IDesadvSapService
	{
		private readonly ServiceLayerClient _sl;
		private readonly IPackResolverService _packResolver;
		private readonly ILogger<DesadvSapService> _logger;

		public DesadvSapService(ServiceLayerClient sl, IPackResolverService packResolver, ILogger<DesadvSapService> logger)
		{
			_sl = sl;
			_packResolver = packResolver;
			_logger = logger;
		}

		public async Task<List<DesadvAlbaran>> GetPendingAlbaranesAsync(
			ClientOptions client, CancellationToken ct)
		{
			var cardCode = client.SapDefaults.CardCode;
			var result = new List<DesadvAlbaran>();

			var filter = Uri.EscapeDataString(
				$"Cancelled eq 'tNO' and CardCode eq '{cardCode}' and " +
				$"(U_SEI_ENVIOMAIL eq '' or U_SEI_ENVIOMAIL eq null or U_SEI_ENVIOMAIL eq '3')");

			var select = "DocEntry,DocNum,NumAtCard,DocDate,DocDueDate,CardCode,DocumentLines,U_SEI_PO_EDI";
			var url = $"DeliveryNotes?$filter={filter}&$select={select}";

			var response = await _sl.GetAsync<SlListResponse<SlDeliveryNote>>(url, ct);

			if (response?.Value == null || response.Value.Count == 0)
			{
				_logger.LogInformation("DESADV: no hay albaranes pendientes para {CardCode}", cardCode);
				return result;
			}

			_logger.LogInformation("DESADV: {Count} albarán(es) pendiente(s) para {CardCode}",
				response.Value.Count, cardCode);

			// GLN del cliente (BY e IV — fijo del BP)
			var glnCliente = await GetGlnClienteAsync(cardCode, ct);

			foreach (var dn in response.Value)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					var albaran = await BuildAlbaranAsync(dn, glnCliente ?? "", ct);
					if (albaran != null) result.Add(albaran);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "DESADV: error procesando albarán DocNum={DocNum}", dn.DocNum);
				}
			}

			return result;
		}

		public async Task MarkAsSentAsync(int docEntry, CancellationToken ct)
		{
			await _sl.PatchAsync($"DeliveryNotes({docEntry})", new { U_SEI_ENVIOMAIL = "1" });
			_logger.LogInformation("DESADV: albarán DocEntry={DocEntry} marcado como enviado.", docEntry);
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
				_logger.LogWarning(ex, "DESADV: error leyendo U_SEIPOEDI del cliente {CardCode}", cardCode);
				return null;
			}
		}

		/// <summary>
		/// Obtiene el GLN del punto de entrega en cascada:
		/// 1. U_SEI_PO_EDI del albarán
		/// 2. U_SEI_PO_EDI del pedido origen (BaseEntry de la primera línea)
		/// 3. U_SEIPOEDI del BP como fallback
		/// </summary>
		private async Task<string?> GetGlnPuntoEntregaAsync(
			SlDeliveryNote dn, string glnClienteFallback, CancellationToken ct)
		{
			// 1. Del albarán directamente
			if (!string.IsNullOrWhiteSpace(dn.U_SEI_PO_EDI))
				return dn.U_SEI_PO_EDI.Trim();

			// 2. Del pedido origen (BaseEntry de la primera línea)
			var baseEntry = dn.DocumentLines?.FirstOrDefault()?.BaseEntry;
			if (baseEntry.HasValue && baseEntry.Value > 0)
			{
				try
				{
					var order = await _sl.GetAsync<SlSalesOrder>(
						$"Orders({baseEntry.Value})?$select=U_SEI_PO_EDI", ct);
					if (!string.IsNullOrWhiteSpace(order?.U_SEI_PO_EDI))
						return order.U_SEI_PO_EDI.Trim();
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "DESADV: no se pudo leer U_SEI_PO_EDI del pedido origen {BaseEntry}", baseEntry);
				}
			}

			// 3. Fallback: mismo GLN del comprador
			_logger.LogWarning(
				"DESADV: albarán {DocNum} sin GLN punto entrega — usando GLN cliente como fallback.",
				dn.DocNum);
			return glnClienteFallback;
		}

		private async Task<DesadvAlbaran?> BuildAlbaranAsync(
			SlDeliveryNote dn, string glnCliente, CancellationToken ct)
		{
			if (dn.DocumentLines == null || dn.DocumentLines.Count == 0)
			{
				_logger.LogWarning("DESADV: albarán {DocNum} sin líneas, se omite.", dn.DocNum);
				return null;
			}

			var glnPuntoEntrega = await GetGlnPuntoEntregaAsync(dn, glnCliente, ct);
			var docNumPedido = await GetDocNumPedidoAsync(dn, ct);

			var albaran = new DesadvAlbaran
			{
				DocEntry = dn.DocEntry,
				DocNum = dn.DocNum.ToString(),
				NumAtCard = dn.NumAtCard?.Trim() ?? "",
				DocDate = FormatDateTime(dn.DocDate),
				DocDueDate = FormatDateTime(dn.DocDueDate),
				GlnCliente = glnCliente,
				GlnPuntoEntrega = glnPuntoEntrega ?? glnCliente,
				DocNumPedido = docNumPedido,
			};

			int bultoNum = 1, lineIdx = 0;

			// Agrupar líneas que pertenecen al mismo pack (mismo U_INTRX_KT_PACK + U_INTRX_KT_QPACK)
			// en una única línea de salida, manteniendo la posición original del documento:
			// el grupo aparece donde aparece su primera línea; las repeticiones posteriores del
			// mismo pack se saltan. Las líneas sueltas (sin pack) se procesan como siempre.
			var cantidadPorGrupo = dn.DocumentLines
				.Where(l => !string.IsNullOrWhiteSpace(l.U_INTRX_KT_PACK))
				.GroupBy(l => (l.U_INTRX_KT_PACK, l.U_INTRX_KT_QPACK))
				.ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

			var gruposEmitidos = new HashSet<(string?, decimal?)>();

			foreach (var ln in dn.DocumentLines)
			{
				ct.ThrowIfCancellationRequested();

				if (!string.IsNullOrWhiteSpace(ln.U_INTRX_KT_PACK))
				{
					var key = (ln.U_INTRX_KT_PACK, ln.U_INTRX_KT_QPACK);
					if (!gruposEmitidos.Add(key))
						continue; // ya se emitió la línea unificada de este pack

					var packCode = ln.U_INTRX_KT_PACK!;
					var pack = await _packResolver.GetPackByCodeAsync(packCode, ct);

					albaran.Lineas.Add(new DesadvLinea
					{
						LineNum = lineIdx,
						Bulto = bultoNum,
						ItemCode = packCode,
						Descripcion = NormalizeText(pack?.Descripcion ?? ""),
						Ean = pack?.Ean ?? "",
						Cantidad = cantidadPorGrupo[key],
					});
				}
				else
				{
					var ean = ln.BarCode?.Trim();
					if (string.IsNullOrWhiteSpace(ean))
						ean = await GetEanFromItemAsync(ln.ItemCode ?? "", ct);

					albaran.Lineas.Add(new DesadvLinea
					{
						LineNum = lineIdx,
						Bulto = bultoNum,
						ItemCode = ln.ItemCode ?? "",
						Descripcion = NormalizeText(ln.ItemDescription ?? ""),
						Ean = ean ?? "",
						Cantidad = ln.Quantity,
					});
				}

				lineIdx++;
				bultoNum++;
			}

			_logger.LogInformation(
				"DESADV: albarán {DocNum} — {Count} línea(s) — GLN_BY={Gln} GLN_DP={Dp}",
				albaran.DocNum, albaran.Lineas.Count, glnCliente, albaran.GlnPuntoEntrega);

			return albaran;
		}

		private async Task<string?> GetEanFromItemAsync(string itemCode, CancellationToken ct)
		{
			if (string.IsNullOrWhiteSpace(itemCode)) return null;
			try
			{
				var escaped = itemCode.Replace("'", "''");
				var item = await _sl.GetAsync<SlItem>($"Items('{escaped}')?$select=BarCode", ct);
				return item?.BarCode?.Trim();
			}
			catch { return null; }
		}

		private static string FormatDateTime(string? sapDate)
		{
			if (string.IsNullOrWhiteSpace(sapDate)) return "";
			if (DateTime.TryParse(sapDate, out var dt)) return dt.ToString("yyyyMMddHHmm");
			return sapDate;
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


		private async Task<string> GetDocNumPedidoAsync(SlDeliveryNote dn, CancellationToken ct)
		{
			try
			{
				var baseEntry = dn.DocumentLines?.FirstOrDefault(l => l.BaseEntry.HasValue && l.BaseEntry.Value > 0)?.BaseEntry;
				if (!baseEntry.HasValue) return "";

				var order = await _sl.GetAsync<SlSalesOrder>(
					$"Orders({baseEntry.Value})?$select=DocNum", ct);

				return order?.DocNum.ToString() ?? "";
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "DESADV: no se pudo leer DocNum del pedido origen.");
				return "";
			}
		}


	}
}