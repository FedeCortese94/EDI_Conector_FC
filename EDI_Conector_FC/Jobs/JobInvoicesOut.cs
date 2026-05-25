using Quartz;
using System;
using System.Threading.Tasks;

namespace EDI_Conector_FC.Jobs
{
	public class JobInvoicesOut : IJob
	{
		public async Task Execute(IJobExecutionContext context)
		{
			Console.WriteLine($"[{DateTime.Now}] Ejecutando JobInvoicesOut");

			await Task.CompletedTask;
		}
	}
}
