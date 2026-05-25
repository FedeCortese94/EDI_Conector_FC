namespace EDI_Conector_FC.Models.SapModels
{
	public class SapOptions
	{
		public string Server { get; set; } = "";
		public string Company { get; set; } = "";
		public string UserName { get; set; } = "";
		public string Password { get; set; } = "";
		public bool IgnoreSslErrors { get; set; }
		public int TimeoutSeconds { get; set; } = 120;
	}
}
