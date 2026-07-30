namespace EDI_Conector_FC.Models.Invoic
{
	public sealed class SlInvoice
	{
		public int DocEntry { get; set; }
		public int DocNum { get; set; }
		public string? NumAtCard { get; set; }
		public string? DocDate { get; set; }
		public string? DocDueDate { get; set; }
		public string? CardCode { get; set; }
		public decimal DocTotal { get; set; }
		public decimal VatSum { get; set; }
		public string? U_SEI_ENVIOMAILF { get; set; }
		public string? U_SEI_PO_EDI { get; set; }          // GLN punto de entrega
		public List<SlInvoiceLine> DocumentLines { get; set; } = new();
	}

	public sealed class SlInvoiceLine
	{
		public int LineNum { get; set; }
		public string? ItemCode { get; set; }
		public string? ItemDescription { get; set; }
		public string? BarCode { get; set; }
		public decimal Quantity { get; set; }
		public decimal Price { get; set; }
		public int? BaseDocNum { get; set; }
		public int? BaseEntry { get; set; }                 // DocEntry del albarán origen
	}

	public sealed class SlInvoicListResponse
	{
		public List<SlInvoice> Value { get; set; } = new();
	}
}