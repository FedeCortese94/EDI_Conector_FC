using EDI_Conector_FC.Models.ClientConfig;
using EDI_Conector_FC.Services.ServiceSAP.Http;
using Microsoft.Extensions.Logging;

namespace EDI_Conector_FC.Services.OrdRsp
{
	public interface IOrdRspSapService
	{
		Task<List<Models.OrdRsp.OrdRspPedido>> GetPendingPedidosAsync(ClientOptions client, CancellationToken ct);
		Task MarkAsSentAsync(int docEntry, CancellationToken ct);
	}

	public sealed class OrdRspSapService : IOrdRspSapService
	{
		private readonly ServiceLayerClient _sl;
		private readonly ILogger<OrdRspSapService> _logger;

		public OrdRspSapService(ServiceLayerClient sl, ILogger<OrdRspSapService> logger)
		{
			_sl = sl;
			_logger = logger;
		}

		public async Task<List<Models.OrdRsp.OrdRspPedido>> GetPendingPedidosAsync(
			ClientOptions client, CancellationToken ct)
		{
			var cardCode = client.SapDefaults.CardCode;
			var result = new List<Models.OrdRsp.OrdRspPedido>();

			// Orders solo devuelve pedidos ya definitivos (confirmados desde su Draft) —
			// un Draft nunca aparece acá, así que no hace falta distinguirlo aparte.
			// U_EDI_ORDRSP (campo nuevo, dedicado): vacío/null/'3' = pendiente (o reenviar),
			// '1' = ya enviado. Los pedidos ya existentes antes de esta funcionalidad se
			// migran a '2' (fuera de este filtro) para que no se tomen todos de golpe.
			var filter = Uri.EscapeDataString(
				$"Cancelled eq 'tNO' and CardCode eq '{cardCode}' and " +
				$"(U_EDI_ORDRSP eq '' or U_EDI_ORDRSP eq null or U_EDI_ORDRSP eq '3')");

			var select = "DocEntry,DocNum,NumAtCard,DocDate,DocDueDate,CardCode,DocumentLines,U_SEI_PO_EDI";
			var url = $"Orders?$filter={filter}&$select={select}";

			var response = await _sl.GetAsync<Models.OrdRsp.SlOrderDocListResponse>(url, ct);

			if (response?.Value == null || response.Value.Count == 0)
			{
				_logger.LogInformation("ORDRSP: no hay pedidos pendientes para {CardCode}", cardCode);
				return result;
			}

			_logger.LogInformation("ORDRSP: {Count} pedido(s) pendiente(s) para {CardCode}",
				response.Value.Count, cardCode);

			var glnCliente = await GetGlnClienteAsync(cardCode, ct);

			foreach (var order in response.Value)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					var pedido = BuildPedido(order, glnCliente ?? "");
					if (pedido != null) result.Add(pedido);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "ORDRSP: error procesando pedido DocNum={DocNum}", order.DocNum);
				}
			}

			return result;
		}

		public async Task MarkAsSentAsync(int docEntry, CancellationToken ct)
		{
			await _sl.PatchAsync($"Orders({docEntry})", new { U_EDI_ORDRSP = "1" });
			_logger.LogInformation("ORDRSP: pedido DocEntry={DocEntry} marcado como enviado.", docEntry);
		}

		// ─────────────────────────────────────────────────────────────────

		private async Task<string?> GetGlnClienteAsync(string cardCode, CancellationToken ct)
		{
			try
			{
				var escaped = cardCode.Replace("'", "''");
				var bp = await _sl.GetAsync<SlBusinessPartnerOrdRsp>(
					$"BusinessPartners('{escaped}')?$select=U_SEIPOEDI", ct);
				return bp?.U_SEIPOEDI?.Trim();
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "ORDRSP: error leyendo U_SEIPOEDI del cliente {CardCode}", cardCode);
				return null;
			}
		}

		private Models.OrdRsp.OrdRspPedido? BuildPedido(Models.OrdRsp.SlOrderDoc order, string glnCliente)
		{
			if (order.DocumentLines == null || order.DocumentLines.Count == 0)
			{
				_logger.LogWarning("ORDRSP: pedido {DocNum} sin líneas, se omite.", order.DocNum);
				return null;
			}

			var pedido = new Models.OrdRsp.OrdRspPedido
			{
				DocEntry = order.DocEntry,
				DocNum = order.DocNum.ToString(),
				NumAtCard = order.NumAtCard?.Trim() ?? "",
				DocDate = FormatDate(order.DocDate),
				DocDueDate = FormatDate(order.DocDueDate),
				GlnCliente = glnCliente,
				GlnPuntoEntrega = !string.IsNullOrWhiteSpace(order.U_SEI_PO_EDI) ? order.U_SEI_PO_EDI.Trim() : glnCliente,
			};

			int lineIdx = 0;
			foreach (var ln in order.DocumentLines)
			{
				pedido.Lineas.Add(new Models.OrdRsp.OrdRspLinea
				{
					LineNum = lineIdx++,
					ItemCode = ln.ItemCode ?? "",
					Ean = ln.BarCode?.Trim() ?? "",
					Cantidad = ln.Quantity,
					Precio = ln.Price,
				});
			}

			_logger.LogInformation(
				"ORDRSP: pedido {DocNum} — {Count} línea(s) — GLN_BY={Gln} GLN_DP={Dp}",
				pedido.DocNum, pedido.Lineas.Count, glnCliente, pedido.GlnPuntoEntrega);

			return pedido;
		}

		private static string FormatDate(string? sapDate)
		{
			if (string.IsNullOrWhiteSpace(sapDate)) return "";
			if (DateTime.TryParse(sapDate, null,
					System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
				return dt.ToString("yyyyMMdd");
			return sapDate.Length > 8 ? sapDate[..8] : sapDate;
		}

		private sealed class SlBusinessPartnerOrdRsp
		{
			public string? U_SEIPOEDI { get; set; }
		}
	}
}
