namespace EDI_Conector_FC.Models.ClientConfig
{
	public sealed class ClientOptions
	{
		public string ClientId { get; set; } = "";
		public string Description { get; set; } = "";
		public bool Enabled { get; set; } = true;

		public ClientFtpOptions Ftp { get; set; } = new();
		public ClientPathsOptions Paths { get; set; } = new();
		public ClientSapDefaults SapDefaults { get; set; } = new();
		public ClientParserOptions Parser { get; set; } = new();
		public ClientDesadvOptions Desadv { get; set; } = new();
		public ClientInvoicOptions? Invoic { get; set; }
	}

	public sealed class ClientFtpOptions
	{
		public bool Enabled { get; set; } = true;
		public string Host { get; set; } = "";
		public int Port { get; set; } = 21;
		public string User { get; set; } = "";
		public string Password { get; set; } = "";
		public string RemoteFolderOrders { get; set; } = "";
		public string FilePrefix { get; set; } = "";
	}

	public sealed class ClientPathsOptions
	{
		// Pedidos entrada
		public string OrdersInbox { get; set; } = "";
		public string OrdersCsv { get; set; } = "";
		public string OrdersProcessed { get; set; } = "";
		public string OrdersError { get; set; } = "";

		// DESADV salida
		public string DesadvOutbox { get; set; } = "";
		public string DesadvProcessed { get; set; } = "";
		public string DesadvError { get; set; } = "";

		// INVOIC salida
		public string InvoicOutbox { get; set; } = "";
		public string InvoicProcessed { get; set; } = "";
		public string InvoicError { get; set; } = "";
	}

	public sealed class ClientSapDefaults
	{
		public string CardCode { get; set; } = "";
		public string WarehouseCode { get; set; } = "";
		public string Currency { get; set; } = "EUR";
		public string CommentsPrefix { get; set; } = "EDI";
	}

	public sealed class ClientParserOptions
	{
		public int EanPadToLength { get; set; } = 0;
	}

	public sealed class ClientDesadvOptions
	{
		public bool Enabled { get; set; } = true;
		public string GlnRemitente { get; set; } = "";
		public string FtpFolderDesadv { get; set; } = "/transactions/desadv/in";
		public bool MarcarComoEnviado { get; set; } = true;
	}

	public sealed class ClientInvoicOptions
	{
		public bool Enabled { get; set; } = true;
		public string GlnRemitente { get; set; } = "";

		/// <summary>GLN fallback si el BP no tiene U_SEIPOEDI informado.</summary>
		public string GlnDestinatarioFallback { get; set; } = "";

		public string FtpFolderInvoic { get; set; } = "/transactions/invoic/in";
		public bool MarcarComoEnviada { get; set; } = true;

		/// <summary>
		/// VAT ID (número identificación fiscal) del comprador.
		/// Va en SINCP BY campo 18. Obligatorio para operaciones intracomunitarias.
		/// Si está vacío se omite (campo queda en blanco).
		/// </summary>
		public string VatIdComprador { get; set; } = "";
	}
}