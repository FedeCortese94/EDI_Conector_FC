using EDI_Conector_FC.Models.ClientConfig;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EDI_Conector_FC.Services.ClientConfig
{
    public interface IClientConfigLoader
    {
        /// <summary>Carga la configuración de un cliente por su ID.</summary>
        ClientOptions Load(string clientId);

        /// <summary>Devuelve todos los clientes habilitados.</summary>
        IReadOnlyList<ClientOptions> LoadAllEnabled();
    }

    public sealed class ClientConfigLoader : IClientConfigLoader
    {
        private readonly ILogger<ClientConfigLoader> _logger;

        // Carpeta base donde viven los ficheros de configuración de cada cliente.
        // Estructura: Clients/{ClientId}/config.json
        private static readonly string ClientsBasePath =
            Path.Combine(AppContext.BaseDirectory, "Clients");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public ClientConfigLoader(ILogger<ClientConfigLoader> logger)
        {
            _logger = logger;
        }

        public ClientOptions Load(string clientId)
        {
            var path = Path.Combine(ClientsBasePath, clientId, "config.json");

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"No se encontró la configuración del cliente '{clientId}'. Ruta esperada: {path}");

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<ClientOptions>(json, JsonOpts)
                ?? throw new InvalidDataException($"El fichero de configuración de '{clientId}' está vacío o es inválido.");

            config.ClientId = clientId; // garantizamos que el ID coincide con el nombre de carpeta
            _logger.LogDebug("Configuración cargada para cliente {ClientId}", clientId);

            return config;
        }

        public IReadOnlyList<ClientOptions> LoadAllEnabled()
        {
            if (!Directory.Exists(ClientsBasePath))
            {
                _logger.LogWarning("Carpeta de clientes no encontrada: {Path}", ClientsBasePath);
                return Array.Empty<ClientOptions>();
            }

            var result = new List<ClientOptions>();

            foreach (var dir in Directory.EnumerateDirectories(ClientsBasePath))
            {
                var clientId = Path.GetFileName(dir);
                try
                {
                    var config = Load(clientId);
                    if (config.Enabled)
                        result.Add(config);
                    else
                        _logger.LogInformation("Cliente {ClientId} deshabilitado, se omite.", clientId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cargando configuración del cliente {ClientId}", clientId);
                }
            }

            _logger.LogInformation("Clientes habilitados cargados: {Count}", result.Count);
            return result;
        }
    }
}
