namespace EDI_Conector_FC.Models.OrdRsp
{
	public sealed class SlOrderDoc
	{
		public int DocEntry { get; set; }
		public int DocNum { get; set; }
		public string? NumAtCard { get; set; }
		public string? DocDate { get; set; }
		public string? DocDueDate { get; set; }
		public string? CardCode { get; set; }
		public string? U_EDI_ORDRSP { get; set; }
		public string? U_SEI_PO_EDI { get; set; }          // GLN punto de entrega del pedido
		public List<SlOrderDocLine> DocumentLines { get; set; } = new();
	}

	public sealed class SlOrderDocLine
	{
		public int LineNum { get; set; }
		public string? ItemCode { get; set; }
		public decimal Quantity { get; set; }
		public decimal Price { get; set; }
		public string? BarCode { get; set; }
	}

	public sealed class SlOrderDocListResponse
	{
		public List<SlOrderDoc> Value { get; set; } = new();
	}
}
