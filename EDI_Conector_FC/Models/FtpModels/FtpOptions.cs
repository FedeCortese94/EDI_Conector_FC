using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EDI_Conector_FC.Models.FtpModels
{
	public class FtpOptions
	{
		public string Host { get; set; } = "";
		public int Port { get; set; }
		public string User { get; set; } = "";
		public string Password { get; set; } = "";
		public string RemoteIn { get; set; } = "";
		public string RemoteOut { get; set; } = "";
		public bool UseSsl { get; set; }
		public bool PassiveMode { get; set; }
	}
}
