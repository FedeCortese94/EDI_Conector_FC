using EDI_Conector_FC.Services.ServiceSAP.Http;
using Microsoft.Extensions.Logging;

namespace EDI_Conector_FC.Services.ServiceSAP
{
    /// <summary>
    /// Resuelve los metadatos necesarios para los campos de usuario del pedido Boozt:
    /// - Temporada (U_INTRX_PR_TEMPORADA / U_SEITemp)
    /// - Marca     (U_INTX_PR_MARCA / U_SEIMarca)
    /// </summary>
    public interface IBooztMetadataService
    {
        Task<string?> GetTemporadaAsync(string tipoDoc, List<string> itemCodes, CancellationToken ct);
        Task<(string? MarcaCodigo, string? MarcaNumero)> GetMarcaAsync(string firstItemCode, CancellationToken ct);
    }

    public sealed class BooztMetadataService : IBooztMetadataService
    {
        private readonly ServiceLayerClient _sl;
        private readonly ILogger<BooztMetadataService> _logger;

        // Mapeo marca código → número para U_SEIMarca
        private static readonly Dictionary<string, string> MarcaMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "DD", "8" },
            { "IJ", "17" },
            { "AB", "1" },
        };

        public BooztMetadataService(ServiceLayerClient sl, ILogger<BooztMetadataService> logger)
        {
            _sl = sl;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────
        // TEMPORADA
        // ─────────────────────────────────────────────────────────────────

        public async Task<string?> GetTemporadaAsync(string tipoDoc, List<string> itemCodes, CancellationToken ct)
        {
            bool esRepeticion = tipoDoc == "224";

            if (esRepeticion)
            {
                // 1) Intentar obtener del primer artículo que tenga temporada
                foreach (var itemCode in itemCodes)
                {
                    var tempArticulo = await GetTemporadaFromItemAsync(itemCode, ct);
                    if (!string.IsNullOrWhiteSpace(tempArticulo))
                    {
                        _logger.LogInformation(
                            "Temporada REPO del artículo {ItemCode}: {Temp}", itemCode, tempArticulo);
                        return tempArticulo;
                    }
                }

                // 2) Fallback tabla maestra U_Bouti = '12'
                _logger.LogInformation("Temporada REPO: ningún artículo tiene temporada, consultando tabla maestra...");
                return await GetTemporadaFromMaestroAsync("12", ct);
            }
            else
            {
                // Inicial: tabla maestra U_Bouti = '11'
                return await GetTemporadaFromMaestroAsync("11", ct);
            }
        }

        private async Task<string?> GetTemporadaFromItemAsync(string itemCode, CancellationToken ct)
        {
            try
            {
                var escaped = itemCode.Replace("'", "''");
                var url = $"Items('{escaped}')?$select=U_INTRX_PR_Temporada,U_SeiTemp";
                var result = await _sl.GetAsync<ItemTemporadaDto>(url, ct);

                if (result == null) return null;

                if (!string.IsNullOrWhiteSpace(result.U_INTRX_PR_Temporada))
                    return result.U_INTRX_PR_Temporada.Trim();

                if (!string.IsNullOrWhiteSpace(result.U_SeiTemp))
                    return result.U_SeiTemp.Trim();

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error leyendo temporada del artículo {ItemCode}", itemCode);
                return null;
            }
        }

        private async Task<string?> GetTemporadaFromMaestroAsync(string bouti, CancellationToken ct)
        {
            try
            {
                // Tabla de usuario via Service Layer
                var url = $"INTRX_FM_TEMPOESHOP?$select=U_Temporada&$filter=U_Bouti eq '{bouti}'&$orderby=U_Temporada desc&$top=1";
                var result = await _sl.GetAsync<SlListResponse<TemporadaMaestroDto>>(url, ct);

                var temp = result?.Value?.FirstOrDefault()?.U_Temporada?.Trim();

                _logger.LogInformation(
                    "Temporada tabla maestra (U_Bouti={Bouti}): {Temp}", bouti, temp ?? "null");

                return temp;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error consultando tabla maestra temporada (U_Bouti={Bouti})", bouti);
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // MARCA
        // ─────────────────────────────────────────────────────────────────

        public async Task<(string? MarcaCodigo, string? MarcaNumero)> GetMarcaAsync(string firstItemCode, CancellationToken ct)
        {
            try
            {
                var escaped = firstItemCode.Replace("'", "''");
                var url = $"Items('{escaped}')?$select=U_INTX_EC_MARCA,U_SeiMarca";
                var result = await _sl.GetAsync<ItemMarcaDto>(url, ct);

                if (result == null) return (null, null);

                // Preferencia U_INTX_EC_MARCA, fallback U_SeiMarca
                var marcaCodigo = !string.IsNullOrWhiteSpace(result.U_INTX_EC_MARCA)
                    ? result.U_INTX_EC_MARCA.Trim()
                    : result.U_SeiMarca?.Trim();

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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error leyendo marca del artículo {ItemCode}", firstItemCode);
                return (null, null);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // DTOs internos
        // ─────────────────────────────────────────────────────────────────

        private sealed class ItemTemporadaDto
        {
            public string? U_INTRX_PR_Temporada { get; set; }
            public string? U_SeiTemp { get; set; }
        }

        private sealed class ItemMarcaDto
        {
            public string? U_INTX_EC_MARCA { get; set; }
            public string? U_SeiMarca { get; set; }
        }

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