using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;
using EDI_Conector_FC.Services.ServiceSAP.Http;

namespace EDI_Conector_FC.Services.ServiceSAP
{
	public interface IItemResolverService
	{
		Task<string?> ResolveItemCodeFromEanAsync(string ean, CancellationToken ct);
	}

	public sealed class ItemResolverService : IItemResolverService
	{
		private readonly ServiceLayerClient _sl;
		private readonly ILogger<ItemResolverService> _logger;

		private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.Ordinal);

		public ItemResolverService(ServiceLayerClient sl, ILogger<ItemResolverService> logger)
		{
			_sl = sl;
			_logger = logger;
		}

		public async Task<string?> ResolveItemCodeFromEanAsync(string ean, CancellationToken ct)
		{
			if (string.IsNullOrWhiteSpace(ean))
				return null;

			ean = ean.Trim();

			if (_cache.TryGetValue(ean, out var cached))
				return cached;

			// 1) buscar tal cual (13)
			var item = await TryResolveAsync(ean, ct);
			if (!string.IsNullOrWhiteSpace(item))
				return Cache(ean, item);

			// 2) si empieza por 0, buscar sin el 0 (12)
			if (ean.Length == 13 && ean.StartsWith("0", StringComparison.Ordinal))
			{
				var ean12 = ean.Substring(1);
				item = await TryResolveAsync(ean12, ct);
				if (!string.IsNullOrWhiteSpace(item))
					return Cache(ean, item);
			}


			_logger.LogWarning("EAN sin correspondencia en SAP (Items.BarCode). EAN={EAN}", ean);
			_cache[ean] = null;
			return null;
		}

		private string Cache(string ean, string itemCode)
		{
			_cache[ean] = itemCode;
			return itemCode;
		}

		private async Task<string?> TryResolveAsync(string ean, CancellationToken ct)
		{
			var eanEscaped = ean.Replace("'", "''");

			// ✅ Campo correcto en Service Layer
			var url = $"Items?$select=ItemCode&$filter=BarCode eq '{eanEscaped}'&$top=1";

			var response = await _sl.GetAsync<ServiceLayerListResponse<ItemCodeDto>>(url, ct);
			return response?.Value?.FirstOrDefault()?.ItemCode;
		}

		private sealed class ItemCodeDto
		{
			public string ItemCode { get; set; } = default!;
		}
	}

	public sealed class ServiceLayerListResponse<T>
	{
		public List<T> Value { get; set; } = new();
	}
}