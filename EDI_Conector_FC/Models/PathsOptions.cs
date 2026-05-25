

namespace EDI_Conector_FC.Models
{
	public class PathsOptions
	{
		public string Base { get; set; }
		public string OrdersInbox { get; set; }
		public string OrdersProcessed { get; set; }
		public string OrdersError { get; set; }
		public string DesadvOutbox { get; set; }
		public string InvoicesOutbox { get; set; }
		public string Logs { get; set; } 
	}
}