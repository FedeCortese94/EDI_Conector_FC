using EDI_Conector_FC.Services.ClientConfig;
using EDI_Conector_FC.Services.CsvGenerator;
using EDI_Conector_FC.Services.OrdersIn;
using EDI_Conector_FC.Services.Remote;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EDI_Conector_FC.Jobs
{
    /// <summary>
    /// Paso 1: Descarga ficheros EDI del FTP y genera el CSV intermedio.
    /// Por cada cliente habilitado con carpeta de pedidos configurada.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class JobOrdersEdiToCsv : IJob
    {
        private readonly ILogger<JobOrdersEdiToCsv> _logger;
        private readonly IClientConfigLoader _clientConfig;
        private readonly IEre1OrdersParser _parser;
        private readonly IOrderCsvGenerator _csvGenerator;
        private readonly IFtpServiceFactory _ftpFactory;

        public JobOrdersEdiToCsv(
            ILogger<JobOrdersEdiToCsv> logger,
            IClientConfigLoader clientConfig,
            IEre1OrdersParser parser,
            IOrderCsvGenerator csvGenerator,
            IFtpServiceFactory ftpFactory)
        {
            _logger = logger;
            _clientConfig = clientConfig;
            _parser = parser;
            _csvGenerator = csvGenerator;
            _ftpFactory = ftpFactory;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("=== JobOrdersEdiToCsv iniciado ===");

            var clients = _clientConfig.LoadAllEnabled();

            if (clients.Count == 0)
            {
                _logger.LogWarning("No hay clientes habilitados. Revisa la carpeta Clients/.");
                return;
            }

            foreach (var client in clients)
            {
                _logger.LogInformation("Procesando cliente: {ClientId}", client.ClientId);

                try
                {
                    await ProcessClientAsync(client, context.CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fatal procesando cliente {ClientId}", client.ClientId);
                }
            }

            _logger.LogInformation("=== JobOrdersEdiToCsv finalizado ===");
        }

        private async Task ProcessClientAsync(
            Models.ClientConfig.ClientOptions client,
            CancellationToken ct)
        {
            var paths = client.Paths;

            Directory.CreateDirectory(paths.OrdersInbox);
            Directory.CreateDirectory(paths.OrdersCsv);
            Directory.CreateDirectory(paths.OrdersProcessed);
            Directory.CreateDirectory(paths.OrdersError);

            // ── 1. Descargar del FTP ──────────────────────────────────────
            var ftp = _ftpFactory.Create(client.Ftp);
            var remoteFiles = await ftp.ListAsync(ct);

            _logger.LogInformation(
                "Cliente {ClientId}: {Count} fichero(s) en FTP",
                client.ClientId, remoteFiles.Count);

            foreach (var rf in remoteFiles)
            {
                ct.ThrowIfCancellationRequested();

                var localPath = Path.Combine(paths.OrdersInbox, rf.Name);

                if (File.Exists(localPath))
                {
                    _logger.LogDebug("Ya descargado: {Name}", rf.Name);
                    continue;
                }

                await ftp.DownloadAsync(rf.Name, localPath, ct);
            }

            // ── 2. Parsear EDI → CSV ──────────────────────────────────────
            var ediFiles = Directory
                .EnumerateFiles(paths.OrdersInbox)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ediFiles.Count == 0)
            {
                _logger.LogInformation("Cliente {ClientId}: inbox vacío.", client.ClientId);
                return;
            }

            _logger.LogInformation(
                "Cliente {ClientId}: {Count} fichero(s) EDI a procesar.",
                client.ClientId, ediFiles.Count);

            foreach (var ediFile in ediFiles)
            {
                ct.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(ediFile);
                _logger.LogInformation("Procesando EDI: {File}", fileName);

                try
                {
                    // Parsear
                    var ediOrder = _parser.ParseFile(ediFile);

                    if (ediOrder.Lines.Count == 0)
                        throw new InvalidOperationException(
                            $"El fichero EDI {fileName} no contiene líneas ERE1L.");

                    // Generar CSV
                    var csvPath = await _csvGenerator.GenerateAsync(ediOrder, client, ct);

                    _logger.LogInformation(
                        "CSV generado correctamente: {CsvFile}", Path.GetFileName(csvPath));

                    // Mover EDI a Processed
                    MoveFile(ediFile, paths.OrdersProcessed, ".EDI_OK");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error procesando EDI {File}", fileName);

                    try { MoveFile(ediFile, paths.OrdersError, ".EDI_ERR"); }
                    catch (Exception moveEx)
                    {
                        _logger.LogError(moveEx, "No se pudo mover a Error: {File}", fileName);
                    }
                }
            }
        }

        private static void MoveFile(string source, string destFolder, string suffix)
        {
            var name = Path.GetFileName(source);
            var dest = Path.Combine(destFolder, name + suffix);

            if (File.Exists(dest))
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
                dest = Path.Combine(destFolder, $"{name}{suffix}.{stamp}");
            }

            File.Move(source, dest);
        }
    }
}
