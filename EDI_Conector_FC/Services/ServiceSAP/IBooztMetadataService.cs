using EDI_Conector_FC.Services.ServiceSAP.Http;
using Microsoft.Extensions.Logging;

namespace EDI_Conector_FC.Services.ServiceSAP
{
	public interface IBooztMetadataService
	{
		Task<string?> GetTemporadaAsync(string tipoDoc, List<string> itemCodes, CancellationToken ct);
		Task<(string? MarcaCodigo, string? MarcaNumero)> GetMarcaAsync(string firstItemCode, CancellationToken ct);
	}

	public sealed class BooztMetadataService : IBooztMetadataService
	{
		private readonly ServiceLayerClient _sl;
		private readonly ILogger<BooztMetadataService> _logger;

		private static readonly Dictionary<string, string> MarcaMap = new(StringComparer.OrdinalIgnoreCase)
		{
			{ "DD", "8"  },
			{ "IJ", "17" },
			{ "AB", "1"  },
		};

		public BooztMetadataService(ServiceLayerClient sl, ILogger<BooztMetadataService> logger)
		{
			_sl = sl;
			_logger = logger;
		}

		// ── TEMPORADA ─────────────────────────────────────────────────────

		public async Task<string?> GetTemporadaAsync(string tipoDoc, List<string> itemCodes, CancellationToken ct)
		{
			bool esRepeticion = tipoDoc == "224";

			if (esRepeticion)
			{
				foreach (var itemCode in itemCodes)
				{
					var temp = await GetTemporadaFromItemAsync(itemCode, ct);
					if (!string.IsNullOrWhiteSpace(temp))
					{
						_logger.LogInformation("Temporada REPO del artículo {ItemCode}: {Temp}", itemCode, temp);
						return temp;
					}
				}
				_logger.LogInformation("Temporada REPO: ningún artículo tiene temporada, consultando tabla maestra...");
				return await GetTemporadaFromMaestroAsync(12, ct);
			}
			else
			{
				return await GetTemporadaFromMaestroAsync(11, ct);
			}
		}

		private async Task<string?> GetTemporadaFromItemAsync(string itemCode, CancellationToken ct)
		{
			// Intentar cada campo por separado — si el campo no existe en SAP, devuelve null sin error
			var temp = await TryGetItemFieldAsync(itemCode, "U_INTRX_PR_Temporada", ct);
			if (!string.IsNullOrWhiteSpace(temp)) return temp;

			temp = await TryGetItemFieldAsync(itemCode, "U_SEITemp", ct);
			if (!string.IsNullOrWhiteSpace(temp)) return temp;

			return null;
		}

		private async Task<string?> GetTemporadaFromMaestroAsync(int bouti, CancellationToken ct)
		{
			try
			{
				var url = $"INTRX_FM_TEMPOESHOP?$select=U_Temporada&$filter=U_Bouti eq {bouti}&$orderby=U_Temporada desc&$top=1";
				var result = await _sl.GetAsync<SlListResponse<TemporadaMaestroDto>>(url, ct);
				var temp = result?.Value?.FirstOrDefault()?.U_Temporada?.Trim();
				_logger.LogInformation("Temporada tabla maestra (U_Bouti={Bouti}): {Temp}", bouti, temp ?? "null");
				return temp;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Error consultando tabla maestra temporada (U_Bouti={Bouti})", bouti);
				return null;
			}
		}

		// ── MARCA ─────────────────────────────────────────────────────────

		public async Task<(string? MarcaCodigo, string? MarcaNumero)> GetMarcaAsync(string firstItemCode, CancellationToken ct)
		{
			var marcaCodigo = await TryGetItemFieldAsync(firstItemCode, "U_INTRX_PR_Marca", ct);

			if (string.IsNullOrWhiteSpace(marcaCodigo))
				marcaCodigo = await TryGetItemFieldAsync(firstItemCode, "U_SEIMarca", ct);

			if (string.IsNullOrWhiteSpace(marcaCodigo))
			{
				_logger.LogWarning("Artículo {ItemCode} sin marca definida.", firstItemCode);
				return (null, null);
			}

			MarcaMap.TryGetValue(marcaCodigo, out var numero);

			_logger.LogInformation(
				"Marca artículo {ItemCode}: Código={Codigo} Número={Numero}",
				firstItemCode, marcaCodigo, numero ?? "?");

			return (marcaCodigo, numero);
		}

		// ── HELPER GENÉRICO ───────────────────────────────────────────────

		/// <summary>
		/// Intenta leer un campo de usuario de un artículo.
		/// Si el campo no existe en SAP devuelve null sin lanzar excepción.
		/// Loguea el nombre del campo que falla para facilitar el diagnóstico.
		/// </summary>
		private async Task<string?> TryGetItemFieldAsync(string itemCode, string fieldName, CancellationToken ct)
		{
			try
			{
				var escaped = itemCode.Replace("'", "''");
				var url = $"Items('{escaped}')?$select={fieldName}";
				var result = await _sl.GetAsync<Dictionary<string, object?>>(url, ct);

				if (result == null || !result.TryGetValue(fieldName, out var val))
					return null;

				var strVal = val?.ToString()?.Trim();
				if (!string.IsNullOrWhiteSpace(strVal))
					_logger.LogDebug("Campo {Field} del artículo {ItemCode}: {Value}", fieldName, itemCode, strVal);

				return strVal;
			}
			catch (Exception)
			{
				// Campo no existe en esta instancia de SAP — log de diagnóstico
				_logger.LogDebug("Campo {Field} no disponible en artículo {ItemCode} — se prueba el siguiente.", fieldName, itemCode);
				return null;
			}
		}

		// ── DTOs ──────────────────────────────────────────────────────────

		private sealed class TemporadaMaestroDto
		{
			public string? U_Temporada { get; set; }
		}

		private sealed class SlListResponse<T>
		{
			public List<T> Value { get; set; } = new();
		}
	}
}