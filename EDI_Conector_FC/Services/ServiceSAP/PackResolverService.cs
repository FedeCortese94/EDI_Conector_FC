using EDI_Conector_FC.Services.ServiceSAP.Http;
using Microsoft.Extensions.Logging;

namespace EDI_Conector_FC.Services.ServiceSAP
{
	public sealed class PackComponent
	{
		public string ItemCode { get; set; } = "";
		public decimal Quantity { get; set; }
	}

	public sealed class PackInfo
	{
		public string Code { get; set; } = "";
		public List<PackComponent> Components { get; set; } = new();
	}

	/// <summary>Datos de cabecera de un pack (EAN y descripción), usados para unificar líneas en DESADV/INVOIC.</summary>
	public sealed class PackHeaderInfo
	{
		public string Code { get; set; } = "";
		public string Ean { get; set; } = "";
		public string Descripcion { get; set; } = "";
	}

	public interface IPackResolverService
	{
		/// <summary>Busca un EAN en @INTRX_KT_PACK (U_CodeBar). Devuelve null si no es un pack.</summary>
		Task<PackInfo?> ResolvePackByEanAsync(string ean, CancellationToken ct);

		/// <summary>Busca un pack por su código (Code) para obtener su EAN/descripción. Devuelve null si no existe.</summary>
		Task<PackHeaderInfo?> GetPackByCodeAsync(string code, CancellationToken ct);
	}

	public sealed class PackResolverService : IPackResolverService
	{
		private readonly ServiceLayerClient _sl;
		private readonly ILogger<PackResolverService> _logger;
		private readonly Dictionary<string, PackHeaderInfo?> _headerCache = new(StringComparer.OrdinalIgnoreCase);

		public PackResolverService(ServiceLayerClient sl, ILogger<PackResolverService> logger)
		{
			_sl = sl;
			_logger = logger;
		}

		public async Task<PackInfo?> ResolvePackByEanAsync(string ean, CancellationToken ct)
		{
			if (string.IsNullOrWhiteSpace(ean))
				return null;

			var eanEscaped = ean.Replace("'", "''");
			var filter = Uri.EscapeDataString($"U_CodeBar eq '{eanEscaped}'");
			var url = $"INTRX_KT_PACK?$filter={filter}&$top=1";

			var response = await _sl.GetAsync<PackListResponse>(url, ct);
			var pack = response?.Value?.FirstOrDefault();

			if (pack is null || string.IsNullOrWhiteSpace(pack.Code))
				return null;

			if (pack.Lines.Count == 0)
			{
				_logger.LogWarning(
					"Pack encontrado sin componentes en @INTRX_KT_LPACK. Code={Code} EAN={Ean}",
					pack.Code, ean);
				return null;
			}

			_logger.LogInformation(
				"EAN {Ean} identificado como pack {Code} — {Count} componente(s).",
				ean, pack.Code, pack.Lines.Count);

			return new PackInfo
			{
				Code = pack.Code,
				Components = pack.Lines
					.Select(l => new PackComponent { ItemCode = l.U_Articulo ?? "", Quantity = l.U_Cantidad })
					.Where(c => !string.IsNullOrWhiteSpace(c.ItemCode))
					.ToList()
			};
		}

		public async Task<PackHeaderInfo?> GetPackByCodeAsync(string code, CancellationToken ct)
		{
			if (string.IsNullOrWhiteSpace(code))
				return null;

			if (_headerCache.TryGetValue(code, out var cached))
				return cached;

			var codeEscaped = code.Replace("'", "''");
			var url = $"INTRX_KT_PACK('{codeEscaped}')?$select=Code,U_CodeBar,U_Desc";

			try
			{
				var pack = await _sl.GetAsync<PackDto>(url, ct);
				if (pack is null || string.IsNullOrWhiteSpace(pack.Code))
				{
					_headerCache[code] = null;
					return null;
				}

				var info = new PackHeaderInfo
				{
					Code = pack.Code,
					Ean = pack.U_CodeBar?.Trim() ?? "",
					Descripcion = pack.U_Desc?.Trim() ?? "",
				};
				_headerCache[code] = info;
				return info;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "No se pudo obtener el pack {Code} desde @INTRX_KT_PACK.", code);
				_headerCache[code] = null;
				return null;
			}
		}

		// DTOs internos — reflejan @INTRX_KT_PACK / @INTRX_KT_LPACK tal como los devuelve Service Layer.
		private sealed class PackListResponse
		{
			public List<PackDto> Value { get; set; } = new();
		}

		private sealed class PackDto
		{
			public string Code { get; set; } = "";
			public string? U_CodeBar { get; set; }
			public string? U_Desc { get; set; }
			public List<PackLineDto> INTRX_KT_LPACKCollection { get; set; } = new();
			public List<PackLineDto> Lines => INTRX_KT_LPACKCollection;
		}

		private sealed class PackLineDto
		{
			public string? U_Articulo { get; set; }
			public decimal U_Cantidad { get; set; }
		}
	}
}
