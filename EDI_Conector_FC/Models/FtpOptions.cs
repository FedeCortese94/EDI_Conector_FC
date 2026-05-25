namespace EDI_Conector_FC.Models
{
	public sealed class FtpOptions
	{
		public string Host { get; set; } = "";
		public int Port { get; set; } = 21;
		public string User { get; set; } = "";
		public string Password { get; set; } = "";
		public string RemoteFolderOrders { get; set; } = "";
		public string FilePrefix { get; set; } = "EDI_";
	}
}
