using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EDI_Conector_FC.Models
{
	public class JobsOptions
	{
		public bool JobOrdersIn { get; set; }
		public bool JobDesadvOut { get; set; }
		public bool JobInvoicesOut { get; set; }
	}
}
