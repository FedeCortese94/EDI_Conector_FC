using EDI_Conector_FC.Services.ClientConfig;
using EDI_Conector_FC.Services.Invoic;
using EDI_Conector_FC.Services.Remote;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EDI_Conector_FC.Jobs
{
	/// <summary>
	/// Job que genera ficheros INVOIC para las facturas pendientes y los sube al FTP.
	/// Procesa una factura a la vez para aislar errores.
	/// </summary>
	[DisallowConcurrentExecution]
	public sealed class JobInvoicesOut : IJob
	{
		private readonly ILogger<JobInvoicesOut> _logger;
		private readonly IClientConfigLoader _clientConfig;
		private readonly IInvoicSapService _sapService;
		private readonly IInvoicFileGenerator _generator;
		private readonly IFtpServiceFactory _ftpFactory;

		public JobInvoicesOut(
			ILogger<JobInvoicesOut> logger,
			IClientConfigLoader clientConfig,
			IInvoicSapService sapService,
			IInvoicFileGenerator generator,
			IFtpServiceFactory ftpFactory)
		{
			_logger = logger;
			_clientConfig = clientConfig;
			_sapService = sapService;
			_generator = generator;
			_ftpFactory = ftpFactory;
		}

		public async Task Execute(IJobExecutionContext context)
		{
			_logger.LogInformation("=== JobInvoicesOut iniciado ===");

			var clients = _clientConfig.LoadAllEnabled()
				.Where(c => c.Invoic?.Enabled == true)
				.ToList();

			if (clients.Count == 0)
			{
				_logger.LogInformation("JobInvoicesOut: ningún cliente con INVOIC habilitado.");
				return;
			}

			foreach (var client in clients)
			{
				_logger.LogInformation("--- INVOIC cliente: {ClientId} ---", client.ClientId);
				try
				{
					await ProcessClientAsync(client, context.CancellationToken);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error fatal INVOIC cliente {ClientId}", client.ClientId);
				}
			}

			_logger.LogInformation("=== JobInvoicesOut finalizado ===");
		}

		private async Task ProcessClientAsync(
			Models.ClientConfig.ClientOptions client, CancellationToken ct)
		{
			var paths = client.Paths;
			var invoic = client.Invoic!;

			Directory.CreateDirectory(paths.InvoicOutbox);
			Directory.CreateDirectory(paths.InvoicProcessed);
			Directory.CreateDirectory(paths.InvoicError);

			// ── 1. Facturas pendientes de SAP ─────────────────────────────
			var facturas = await _sapService.GetPendingFacturasAsync(client, ct);

			if (facturas.Count == 0)
			{
				_logger.LogInformation("INVOIC {ClientId}: no hay facturas pendientes.", client.ClientId);
				return;
			}

			_logger.LogInformation(
				"INVOIC {ClientId}: {Count} factura(s) a procesar.", client.ClientId, facturas.Count);

			// ── 2. Procesar una a una ─────────────────────────────────────
			foreach (var factura in facturas)
			{
				ct.ThrowIfCancellationRequested();

				_logger.LogInformation("INVOIC: procesando factura {DocNum}", factura.DocNum);

				string? filePath = null;
				string? uploadedRemotePath = null;

				try
				{
					// Generar fichero
					filePath = await _generator.GenerateAsync(factura, client, ct);

					// Subir al FTP
					uploadedRemotePath = await UploadToFtpAsync(filePath, client, ct);

					// Marcar como enviada en SAP
					if (invoic.MarcarComoEnviada)
						await _sapService.MarkAsSentAsync(factura.DocEntry, ct);

					// Mover a Processed
					MoveFile(filePath, paths.InvoicProcessed, ".OK");

					_logger.LogInformation(
						"INVOIC OK: factura {DocNum} enviada y marcada.", factura.DocNum);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "INVOIC ERROR: factura {DocNum}", factura.DocNum);

					// Rollback: si ya se subió al FTP pero falló el marcado en SAP,
					// borramos el remoto para no duplicarlo en el próximo intento.
					if (uploadedRemotePath != null)
					{
						try
						{
							var ftp = _ftpFactory.Create(client.Ftp);
							await ftp.DeleteAsync(uploadedRemotePath, ct);
							_logger.LogWarning(
								"INVOIC: rollback FTP — eliminado tras fallo posterior: {RemotePath}",
								uploadedRemotePath);
						}
						catch (Exception exFtp)
						{
							_logger.LogError(exFtp,
								"INVOIC: no se pudo hacer rollback del FTP para {RemotePath}",
								uploadedRemotePath);
						}
					}

					if (filePath != null && File.Exists(filePath))
					{
						try { MoveFile(filePath, paths.InvoicError, ".ERR"); }
						catch (Exception moveEx)
						{
							_logger.LogError(moveEx,
								"INVOIC: no se pudo mover a Error: {File}", filePath);
						}
					}
				}
			}
		}

		/// <returns>La ruta remota si se subió, o null si el FTP está deshabilitado.</returns>
		private async Task<string?> UploadToFtpAsync(
			string localFile,
			Models.ClientConfig.ClientOptions client,
			CancellationToken ct)
		{
			if (!client.Ftp.Enabled)
			{
				_logger.LogInformation(
					"INVOIC: FTP deshabilitado — fichero generado localmente en {Path}", localFile);
				return null;
			}

			var ftp = _ftpFactory.Create(client.Ftp);
			var fileName = Path.GetFileName(localFile);
			var remotePath = $"{client.Invoic!.FtpFolderInvoic}/{fileName}";

			await ftp.UploadAsync(localFile, remotePath, ct);

			_logger.LogInformation("INVOIC: subido al FTP: {RemotePath}", remotePath);

			return remotePath;
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