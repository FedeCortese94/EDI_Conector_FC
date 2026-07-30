using EDI_Conector_FC.Services.ClientConfig;
using EDI_Conector_FC.Services.Desadv;
using EDI_Conector_FC.Services.Remote;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EDI_Conector_FC.Jobs
{
	/// <summary>
	/// Job que genera ficheros DESADV para los albaranes pendientes y los sube al FTP.
	/// Se ejecuta cada X minutos según configuración.
	/// Procesa un albarán a la vez para aislar errores.
	/// </summary>
	[DisallowConcurrentExecution]
	public sealed class JobDesadvOut : IJob
	{
		private readonly ILogger<JobDesadvOut> _logger;
		private readonly IClientConfigLoader _clientConfig;
		private readonly IDesadvSapService _sapService;
		private readonly IDesadvFileGenerator _generator;
		private readonly IFtpServiceFactory _ftpFactory;

		public JobDesadvOut(
			ILogger<JobDesadvOut> logger,
			IClientConfigLoader clientConfig,
			IDesadvSapService sapService,
			IDesadvFileGenerator generator,
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
			_logger.LogInformation("=== JobDesadvOut iniciado ===");

			var clients = _clientConfig.LoadAllEnabled()
				.Where(c => c.Desadv.Enabled)
				.ToList();

			if (clients.Count == 0)
			{
				_logger.LogInformation("JobDesadvOut: ningún cliente con DESADV habilitado.");
				return;
			}

			foreach (var client in clients)
			{
				_logger.LogInformation("--- DESADV cliente: {ClientId} ---", client.ClientId);
				try
				{
					await ProcessClientAsync(client, context.CancellationToken);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error fatal DESADV cliente {ClientId}", client.ClientId);
				}
			}

			_logger.LogInformation("=== JobDesadvOut finalizado ===");
		}

		private async Task ProcessClientAsync(
			Models.ClientConfig.ClientOptions client, CancellationToken ct)
		{
			var paths = client.Paths;
			var desadv = client.Desadv;

			Directory.CreateDirectory(paths.DesadvOutbox);
			Directory.CreateDirectory(paths.DesadvProcessed);
			Directory.CreateDirectory(paths.DesadvError);

			// ── 1. Obtener albaranes pendientes de SAP ────────────────────
			var albaranes = await _sapService.GetPendingAlbaranesAsync(client, ct);

			if (albaranes.Count == 0)
			{
				_logger.LogInformation(
					"DESADV {ClientId}: no hay albaranes pendientes.", client.ClientId);
				return;
			}

			_logger.LogInformation(
				"DESADV {ClientId}: {Count} albarán(es) a procesar.", client.ClientId, albaranes.Count);

			// ── 2. Procesar uno a uno ─────────────────────────────────────
			foreach (var albaran in albaranes)
			{
				ct.ThrowIfCancellationRequested();

				_logger.LogInformation(
					"DESADV: procesando albarán {DocNum}", albaran.DocNum);

				string? filePath = null;
				string? uploadedRemotePath = null;

				try
				{
					// Generar fichero
					filePath = await _generator.GenerateAsync(albaran, client, ct);

					// Subir al FTP
					uploadedRemotePath = await UploadToFtpAsync(filePath, client, ct);

					// Marcar como enviado en SAP
					if (desadv.MarcarComoEnviado)
						await _sapService.MarkAsSentAsync(albaran.DocEntry, ct);

					// Mover a Processed
					MoveFile(filePath, paths.DesadvProcessed, ".OK");

					_logger.LogInformation(
						"DESADV OK: albarán {DocNum} enviado y marcado.", albaran.DocNum);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "DESADV ERROR: albarán {DocNum}", albaran.DocNum);

					// Rollback: si ya se subió al FTP pero falló el marcado en SAP,
					// borramos el remoto para no duplicarlo en el próximo intento.
					if (uploadedRemotePath != null)
					{
						try
						{
							var ftp = _ftpFactory.Create(client.Ftp);
							await ftp.DeleteAsync(uploadedRemotePath, ct);
							_logger.LogWarning(
								"DESADV: rollback FTP — eliminado tras fallo posterior: {RemotePath}",
								uploadedRemotePath);
						}
						catch (Exception exFtp)
						{
							_logger.LogError(exFtp,
								"DESADV: no se pudo hacer rollback del FTP para {RemotePath}",
								uploadedRemotePath);
						}
					}

					if (filePath != null && File.Exists(filePath))
					{
						try { MoveFile(filePath, paths.DesadvError, ".ERR"); }
						catch (Exception moveEx)
						{
							_logger.LogError(moveEx,
								"DESADV: no se pudo mover a Error: {File}", filePath);
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
					"DESADV: FTP deshabilitado — fichero generado localmente en {Path}", localFile);
				return null;
			}

			var ftp = _ftpFactory.Create(client.Ftp);
			var fileName = Path.GetFileName(localFile);
			var remotePath = $"{client.Desadv.FtpFolderDesadv}/{fileName}";

			await ftp.UploadAsync(localFile, remotePath, ct);

			_logger.LogInformation("DESADV: subido al FTP: {RemotePath}", remotePath);

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