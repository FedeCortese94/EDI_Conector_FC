using EDI_Conector_FC.Services.ClientConfig;
using EDI_Conector_FC.Services.OrdRsp;
using EDI_Conector_FC.Services.Remote;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EDI_Conector_FC.Jobs
{
	/// <summary>
	/// Job que genera el ORDRSP (confirmación) para los pedidos que pasaron de Draft a
	/// definitivos en SAP, con las cantidades y precios realmente confirmados, y lo sube
	/// al FTP. Procesa un pedido a la vez para aislar errores.
	/// </summary>
	[DisallowConcurrentExecution]
	public sealed class JobOrdRspOut : IJob
	{
		private readonly ILogger<JobOrdRspOut> _logger;
		private readonly IClientConfigLoader _clientConfig;
		private readonly IOrdRspSapService _sapService;
		private readonly IOrdRspFileGenerator _generator;
		private readonly IFtpServiceFactory _ftpFactory;

		public JobOrdRspOut(
			ILogger<JobOrdRspOut> logger,
			IClientConfigLoader clientConfig,
			IOrdRspSapService sapService,
			IOrdRspFileGenerator generator,
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
			_logger.LogInformation("=== JobOrdRspOut iniciado ===");

			var clients = _clientConfig.LoadAllEnabled()
				.Where(c => c.OrdRsp?.Enabled == true)
				.ToList();

			if (clients.Count == 0)
			{
				_logger.LogInformation("JobOrdRspOut: ningún cliente con ORDRSP habilitado.");
				return;
			}

			foreach (var client in clients)
			{
				_logger.LogInformation("--- ORDRSP cliente: {ClientId} ---", client.ClientId);
				try
				{
					await ProcessClientAsync(client, context.CancellationToken);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error fatal ORDRSP cliente {ClientId}", client.ClientId);
				}
			}

			_logger.LogInformation("=== JobOrdRspOut finalizado ===");
		}

		private async Task ProcessClientAsync(
			Models.ClientConfig.ClientOptions client, CancellationToken ct)
		{
			var paths = client.Paths;
			var ordrsp = client.OrdRsp!;

			Directory.CreateDirectory(paths.OrdRspOutbox);
			Directory.CreateDirectory(paths.OrdRspProcessed);
			Directory.CreateDirectory(paths.OrdRspError);

			// ── 1. Pedidos pendientes de confirmar (ya definitivos en SAP) ──
			var pedidos = await _sapService.GetPendingPedidosAsync(client, ct);

			if (pedidos.Count == 0)
			{
				_logger.LogInformation(
					"ORDRSP {ClientId}: no hay pedidos pendientes.", client.ClientId);
				return;
			}

			_logger.LogInformation(
				"ORDRSP {ClientId}: {Count} pedido(s) a procesar.", client.ClientId, pedidos.Count);

			// ── 2. Procesar uno a uno ─────────────────────────────────────
			foreach (var pedido in pedidos)
			{
				ct.ThrowIfCancellationRequested();

				_logger.LogInformation(
					"ORDRSP: procesando pedido {DocNum}", pedido.DocNum);

				string? filePath = null;
				string? uploadedRemotePath = null;

				try
				{
					// Generar fichero
					filePath = await _generator.GenerateAsync(pedido, client, ct);

					// Subir al FTP
					uploadedRemotePath = await UploadToFtpAsync(filePath, client, ct);

					// Marcar como enviado en SAP
					if (ordrsp.MarcarComoEnviado)
						await _sapService.MarkAsSentAsync(pedido.DocEntry, ct);

					// Mover a Processed
					MoveFile(filePath, paths.OrdRspProcessed, ".OK");

					_logger.LogInformation(
						"ORDRSP OK: pedido {DocNum} enviado y marcado.", pedido.DocNum);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "ORDRSP ERROR: pedido {DocNum}", pedido.DocNum);

					// Rollback: si ya se subió al FTP pero falló el marcado en SAP,
					// borramos el remoto para no duplicarlo en el próximo intento.
					if (uploadedRemotePath != null)
					{
						try
						{
							var ftp = _ftpFactory.Create(client.Ftp);
							await ftp.DeleteAsync(uploadedRemotePath, ct);
							_logger.LogWarning(
								"ORDRSP: rollback FTP — eliminado tras fallo posterior: {RemotePath}",
								uploadedRemotePath);
						}
						catch (Exception exFtp)
						{
							_logger.LogError(exFtp,
								"ORDRSP: no se pudo hacer rollback del FTP para {RemotePath}",
								uploadedRemotePath);
						}
					}

					if (filePath != null && File.Exists(filePath))
					{
						try { MoveFile(filePath, paths.OrdRspError, ".ERR"); }
						catch (Exception moveEx)
						{
							_logger.LogError(moveEx,
								"ORDRSP: no se pudo mover a Error: {File}", filePath);
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
					"ORDRSP: FTP deshabilitado — fichero generado localmente en {Path}", localFile);
				return null;
			}

			var ftp = _ftpFactory.Create(client.Ftp);
			var fileName = Path.GetFileName(localFile);
			var remotePath = $"{client.OrdRsp!.FtpFolderOrdRsp}/{fileName}";

			await ftp.UploadAsync(localFile, remotePath, ct);

			_logger.LogInformation("ORDRSP: subido al FTP: {RemotePath}", remotePath);

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
