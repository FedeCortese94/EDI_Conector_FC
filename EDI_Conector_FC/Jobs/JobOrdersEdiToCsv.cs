using EDI_Conector_FC.Models.ClientConfig;
using EDI_Conector_FC.Services.ClientConfig;
using EDI_Conector_FC.Services.CsvGenerator;
using EDI_Conector_FC.Services.OrdersIn;
using EDI_Conector_FC.Services.Remote;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EDI_Conector_FC.Jobs
{
    /// <summary>
    /// Paso 1: Descarga ficheros EDI del FTP (si está habilitado) y genera el CSV intermedio.
    /// Si Ftp.Enabled = false, trabaja directamente con los ficheros del Inbox local.
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
                _logger.LogInformation("--- Procesando cliente: {ClientId} ---", client.ClientId);
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

        // ─────────────────────────────────────────────────────────────────────
        private async Task ProcessClientAsync(ClientOptions client, CancellationToken ct)
        {
            var paths = client.Paths;

            // Asegurar que existen todas las carpetas necesarias
            Directory.CreateDirectory(paths.OrdersInbox);
            Directory.CreateDirectory(paths.OrdersCsv);
            Directory.CreateDirectory(paths.OrdersProcessed);
            Directory.CreateDirectory(paths.OrdersError);

            // ── 1. Descarga FTP (opcional) ────────────────────────────────
            if (client.Ftp.Enabled)
            {
                await DownloadFromFtpAsync(client, ct);
            }
            else
            {
                _logger.LogInformation(
                    "Cliente {ClientId}: FTP deshabilitado — usando ficheros locales en {Inbox}",
                    client.ClientId, paths.OrdersInbox);
            }

            // ── 2. Leer ficheros EDI del Inbox ────────────────────────────
            var ediFiles = Directory
                .EnumerateFiles(paths.OrdersInbox)
                .Where(f => !f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) // por si Inbox y CSV son la misma carpeta
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ediFiles.Count == 0)
            {
                _logger.LogInformation(
                    "Cliente {ClientId}: Inbox vacío ({Inbox}), nada que procesar.",
                    client.ClientId, paths.OrdersInbox);
                return;
            }

            _logger.LogInformation(
                "Cliente {ClientId}: {Count} fichero(s) EDI encontrados.",
                client.ClientId, ediFiles.Count);

            // ── 3. Procesar cada fichero EDI ──────────────────────────────
            foreach (var ediFile in ediFiles)
            {
                ct.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(ediFile);
                _logger.LogInformation("Procesando EDI: {File}", fileName);

                try
                {
                    // Parse EDI → EdiOrder
                    var ediOrder = _parser.ParseFile(ediFile);

                    if (ediOrder.Lines.Count == 0)
                        throw new InvalidOperationException(
                            $"El fichero {fileName} no contiene líneas ERE1L.");

                    _logger.LogInformation(
                        "Parse OK — EdiDocNum={EdiDocNum} Líneas={Lines}",
                        ediOrder.EdiDocNum, ediOrder.Lines.Count);

                    // EdiOrder → CSV
                    var csvPath = await _csvGenerator.GenerateAsync(ediOrder, client, ct);

                    _logger.LogInformation(
                        "CSV generado: {CsvFile}", Path.GetFileName(csvPath));

                    // Mover EDI a Processed (solo si Inbox ≠ CSV)
                    if (!string.Equals(
                            Path.GetFullPath(paths.OrdersInbox),
                            Path.GetFullPath(paths.OrdersProcessed),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        MoveFile(ediFile, paths.OrdersProcessed, ".EDI_OK");
                        _logger.LogInformation("EDI movido a Processed: {File}", fileName);
                    }
                    else
                    {
                        // Inbox y Processed son la misma carpeta (modo TEST):
                        // renombramos en el mismo sitio para no reprocesarlo
                        var renamedPath = ediFile + ".EDI_OK";
                        File.Move(ediFile, renamedPath, overwrite: true);
                        _logger.LogInformation(
                            "Modo TEST: EDI renombrado a {File}", Path.GetFileName(renamedPath));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error procesando EDI {File}", fileName);

                    try
                    {
                        // Intentar mover a Error
                        if (!string.Equals(
                                Path.GetFullPath(paths.OrdersInbox),
                                Path.GetFullPath(paths.OrdersError),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            MoveFile(ediFile, paths.OrdersError, ".EDI_ERR");
                        }
                        else
                        {
                            File.Move(ediFile, ediFile + ".EDI_ERR", overwrite: true);
                        }
                    }
                    catch (Exception moveEx)
                    {
                        _logger.LogError(moveEx, "No se pudo mover a Error: {File}", fileName);
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        private async Task DownloadFromFtpAsync(ClientOptions client, CancellationToken ct)
        {
            _logger.LogInformation(
                "Cliente {ClientId}: conectando FTP {Host}:{Port}",
                client.ClientId, client.Ftp.Host, client.Ftp.Port);

            var ftp = _ftpFactory.Create(client.Ftp);
            var remoteFiles = await ftp.ListAsync(ct);

            _logger.LogInformation(
                "Cliente {ClientId}: {Count} fichero(s) en FTP",
                client.ClientId, remoteFiles.Count);

            foreach (var rf in remoteFiles)
            {
                ct.ThrowIfCancellationRequested();

                var localPath = Path.Combine(client.Paths.OrdersInbox, rf.Name);

                if (File.Exists(localPath))
                {
                    _logger.LogDebug("Ya existe local, se omite: {Name}", rf.Name);
                    continue;
                }

                await ftp.DownloadAsync(rf.Name, localPath, ct);
                _logger.LogInformation("Descargado: {Name}", rf.Name);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        private static void MoveFile(string source, string destFolder, string suffix)
        {
            Directory.CreateDirectory(destFolder);
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
