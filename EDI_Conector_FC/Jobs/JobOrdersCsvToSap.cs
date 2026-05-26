using EDI_Conector_FC.Services.ClientConfig;
using EDI_Conector_FC.Services.CsvImporter;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EDI_Conector_FC.Jobs
{
    /// <summary>
    /// Paso 2: Lee los CSV intermedios y los importa a SAP B1.
    /// Se ejecuta después de JobOrdersEdiToCsv (o manualmente tras revisión).
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class JobOrdersCsvToSap : IJob
    {
        private readonly ILogger<JobOrdersCsvToSap> _logger;
        private readonly IClientConfigLoader _clientConfig;
        private readonly IOrderCsvImporter _importer;

        public JobOrdersCsvToSap(
            ILogger<JobOrdersCsvToSap> logger,
            IClientConfigLoader clientConfig,
            IOrderCsvImporter importer)
        {
            _logger = logger;
            _clientConfig = clientConfig;
            _importer = importer;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("=== JobOrdersCsvToSap iniciado ===");

            var clients = _clientConfig.LoadAllEnabled();

            foreach (var client in clients)
            {
                _logger.LogInformation("Importando CSVs del cliente: {ClientId}", client.ClientId);

                try
                {
                    await ProcessClientAsync(client, context.CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fatal importando CSVs del cliente {ClientId}", client.ClientId);
                }
            }

            _logger.LogInformation("=== JobOrdersCsvToSap finalizado ===");
        }

        private async Task ProcessClientAsync(
            Models.ClientConfig.ClientOptions client,
            CancellationToken ct)
        {
            var csvFolder      = client.Paths.OrdersCsv;
            var processedFolder = client.Paths.OrdersProcessed;
            var errorFolder    = client.Paths.OrdersError;

            if (!Directory.Exists(csvFolder))
            {
                _logger.LogInformation(
                    "Cliente {ClientId}: carpeta CSV no existe, nada que importar.", client.ClientId);
                return;
            }

            // Solo ficheros .csv (no los ya procesados)
            var csvFiles = Directory
                .EnumerateFiles(csvFolder, "*.csv")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (csvFiles.Count == 0)
            {
                _logger.LogInformation("Cliente {ClientId}: no hay CSVs pendientes.", client.ClientId);
                return;
            }

            _logger.LogInformation(
                "Cliente {ClientId}: {Count} CSV(s) a importar.", client.ClientId, csvFiles.Count);

            foreach (var csvFile in csvFiles)
            {
                ct.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(csvFile);
                _logger.LogInformation("Importando CSV: {File}", fileName);

                try
                {
                    var response = await _importer.ImportAsync(csvFile, client, ct);

                    _logger.LogInformation(
                        "SAP OK — DocEntry={DocEntry} DocNum={DocNum} Fichero={File}",
                        response.DocEntry, response.DocNum, fileName);

                    // Mover CSV a Processed con sufijo .SAP_OK
                    MoveFile(csvFile, processedFolder, ".SAP_OK");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error importando CSV {File}", fileName);

                    try { MoveFile(csvFile, errorFolder, ".SAP_ERR"); }
                    catch (Exception moveEx)
                    {
                        _logger.LogError(moveEx, "No se pudo mover a Error: {File}", fileName);
                    }
                }
            }
        }

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
