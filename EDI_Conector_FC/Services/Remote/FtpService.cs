using EDI_Conector_FC.Models;
using FluentFTP;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace EDI_Conector_FC.Services.Remote
{
	public interface IFtpService
	{
		Task<List<FtpListItem>> ListAsync(CancellationToken ct);
		Task DownloadAsync(string remoteFileName, string localFullPath, CancellationToken ct);
		Task DeleteAsync(string remoteFileName, CancellationToken ct);
	}

	public sealed class FtpService : IFtpService
	{
		private readonly ILogger<FtpService> _logger;
		private readonly FtpOptions _opt;

		public FtpService(ILogger<FtpService> logger, IOptions<FtpOptions> opt)
		{
			_logger = logger;
			_opt = opt.Value;
		}

		private AsyncFtpClient CreateClient()
		{
			var cred = new NetworkCredential(_opt.User, _opt.Password);
			var client = new AsyncFtpClient(_opt.Host, cred, _opt.Port);

			// FTP clásico
			client.Config.EncryptionMode = FtpEncryptionMode.None;
			client.Config.ValidateAnyCertificate = false;
			client.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;

			return client;
		}

		public async Task<List<FtpListItem>> ListAsync(CancellationToken ct)
		{
			await using var client = CreateClient();
			await client.Connect(ct);

			if (!string.IsNullOrWhiteSpace(_opt.RemoteFolderOrders))
				await client.SetWorkingDirectory(_opt.RemoteFolderOrders, ct);

			var items = (await client.GetListing(ct)).ToList();

			// Solo ficheros + prefijo si aplica
			var files = items
				.Where(i => i.Type == FtpObjectType.File)
				.Where(i => string.IsNullOrWhiteSpace(_opt.FilePrefix) || i.Name.StartsWith(_opt.FilePrefix, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(i => i.Modified) // más recientes primero
				.ToList();

			return files;
		}

		public async Task DownloadAsync(string remoteFileName, string localFullPath, CancellationToken ct)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(localFullPath)!);

			await using var client = CreateClient();
			await client.Connect(ct);

			if (!string.IsNullOrWhiteSpace(_opt.RemoteFolderOrders))
				await client.SetWorkingDirectory(_opt.RemoteFolderOrders, ct);

			var status = await client.DownloadFile(localFullPath,
													remoteFileName,
													FtpLocalExists.Overwrite,
													FtpVerify.None,
													progress: null,
													token: ct);

			if (status != FtpStatus.Success)
				throw new Exception($"FTP download falló: {remoteFileName} -> {localFullPath}. Status={status}");

			_logger.LogInformation("FTP descargado: {Remote} -> {Local}", remoteFileName, localFullPath);
		}

		public async Task DeleteAsync(string remoteFileName, CancellationToken ct)
		{
			await using var client = CreateClient();
			await client.Connect(ct);

			if (!string.IsNullOrWhiteSpace(_opt.RemoteFolderOrders))
				await client.SetWorkingDirectory(_opt.RemoteFolderOrders, ct);

			await client.DeleteFile(remoteFileName, ct);
			_logger.LogInformation("FTP eliminado: {Remote}", remoteFileName);
		}
	}
}
