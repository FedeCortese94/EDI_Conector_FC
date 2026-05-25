using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EDI_Conector_FC.Models
{
	public class QuartzScheduleOptions
	{
		public string JobOrdersIn { get; set; }
		public string JobDesadvOut { get; set; }
		public string JobInvoicesOut { get; set; }
	}
}
