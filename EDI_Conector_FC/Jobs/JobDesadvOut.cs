
using Quartz;
using System.Threading.Tasks;
using System;

namespace EDI_Conector_FC.Jobs
{
	public class JobDesadvOut : IJob
	{
		public async Task Execute(IJobExecutionContext context)
		{
			Console.WriteLine($"[{DateTime.Now}] Ejecutando JobDesadvOut");

			await Task.CompletedTask;
		}
	}
}
