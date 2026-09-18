namespace EDI_Conector_FC.Models.Desadv
{
	public sealed class SlDeliveryNote
	{
		public int DocEntry { get; set; }
		public int DocNum { get; set; }
		public string? NumAtCard { get; set; }
		public string? DocDate { get; set; }
		public string? DocDueDate { get; set; }
		public string? CardCode { get; set; }
		public string? U_SEI_ENVIOMAIL { get; set; }
		public string? U_SEI_PO_EDI { get; set; }          // GLN punto de entrega del albarán
		public List<SlDeliveryLine> DocumentLines { get; set; } = new();
	}

	public sealed class SlDeliveryLine
	{
		public int LineNum { get; set; }
		public string? ItemCode { get; set; }
		public string? ItemDescription { get; set; }
		public decimal Quantity { get; set; }
		public string? BarCode { get; set; }
		public int? BaseEntry { get; set; }                 // DocEntry del pedido origen
		public string? U_INTRX_KT_PACK { get; set; }
		public decimal? U_INTRX_KT_QPACK { get; set; }
	}

	public sealed class SlSalesOrder
	{
		public int DocEntry { get; set; }
		public string? U_SEI_PO_EDI { get; set; }          // GLN punto de entrega del pedido

		public int DocNum { get; set; }
	}

	public sealed class SlItem
	{
		public string? ItemCode { get; set; }
		public string? BarCode { get; set; }
	}

	public sealed class SlBusinessPartner
	{
		public string? CardCode { get; set; }
		public string? U_SEIPOEDI { get; set; }
	}

	public sealed class SlListResponse<T>
	{
		public List<T> Value { get; set; } = new();
	}
}