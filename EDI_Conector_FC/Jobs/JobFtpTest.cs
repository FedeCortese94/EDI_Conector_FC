using EDI_Conector_FC.Services.Remote;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EDI_Conector_FC.Jobs
{
	[DisallowConcurrentExecution]
	public sealed class JobFtpTest : IJob
	{
		private readonly ILogger<JobFtpTest> _logger;
		private readonly IFtpService _ftp;

		public JobFtpTest(ILogger<JobFtpTest> logger, IFtpService ftp)
		{
			_logger = logger;
			_ftp = ftp;
		}

		public async Task Execute(IJobExecutionContext context)
		{
			_logger.LogInformation("=== Ejecutando JobFtpTest (FTP LIST) ===");

			try
			{
				var items = await _ftp.ListAsync(context.CancellationToken);

				_logger.LogInformation("FTP OK. Ficheros encontrados: {Count}", items.Count);

				foreach (var f in items.Take(5))
				{
					_logger.LogInformation("FTP FILE: {Name} Modified={Modified} Size={Size}",
						f.Name, f.Modified, f.Size);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "FTP ERROR en JobFtpTest");
			}
		}
	}
}
