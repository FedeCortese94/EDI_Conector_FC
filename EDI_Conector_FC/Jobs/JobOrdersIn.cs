using EDI_Conector_FC.Services.OrdersIn;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EDI_Conector_FC.Jobs
{
	[DisallowConcurrentExecution]
	public class JobOrdersIn : IJob
	{
		private readonly ILogger<JobOrdersIn> _logger;
		private readonly IOrdersInProcessor _processor;

		public JobOrdersIn(ILogger<JobOrdersIn> logger, IOrdersInProcessor processor)
		{
			_logger = logger;
			_processor = processor;
		}

		public async Task Execute(IJobExecutionContext context)
		{
			_logger.LogInformation("=== Ejecutando JobOrdersIn ===");

			try
			{
				await _processor.ProcessAsync(context.CancellationToken);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error fatal en JobOrdersIn");
			}
		}
	}
}
