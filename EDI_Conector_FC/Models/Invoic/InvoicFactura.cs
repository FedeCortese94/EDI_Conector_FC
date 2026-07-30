namespace EDI_Conector_FC.Models.Invoic
{
	public sealed class InvoicFactura
	{
		public int DocEntry { get; set; }
		public string DocNum { get; set; } = "";
		public string NumAtCard { get; set; } = "";
		public string AlbaranOrig { get; set; } = "";
		public string DocDate { get; set; } = "";
		public string DocDueDate { get; set; } = "";

		/// <summary>GLN comprador e facturado (BY/IV) — de OCRD.U_SEIPOEDI</summary>
		public string GlnCliente { get; set; } = "";

		/// <summary>GLN punto de entrega (DP) — de U_SEI_PO_EDI en cascada</summary>
		public string GlnPuntoEntrega { get; set; } = "";

		public decimal DocTotal { get; set; }
		public decimal VatSum { get; set; }
		public decimal Neto => DocTotal - VatSum;
		public decimal Total => DocTotal;

		public List<InvoicLinea> Lineas { get; set; } = new();
	}

	public sealed class InvoicLinea
	{
		public int LineNum { get; set; }
		public string ItemCode { get; set; } = "";
		public string Ean { get; set; } = "";        // ← añadir
		public string Descripcion { get; set; } = "";
		public decimal Quantity { get; set; }
		public decimal Price { get; set; }
		public decimal TotalLinea => Quantity * Price;
	}
}