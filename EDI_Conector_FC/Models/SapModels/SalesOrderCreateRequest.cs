namespace EDI_Conector_FC.Models.SapModels
{
	public sealed class SalesOrderCreateRequest
	{
		public string CardCode { get; set; } = "";
		public string DocDate { get; set; } = "";    // yyyy-MM-dd
		public string DocDueDate { get; set; } = ""; // yyyy-MM-dd

		// referencia cliente (muy útil para evitar duplicados)
		public string? NumAtCard { get; set; }

		public List<SalesOrderLine> DocumentLines { get; set; } = new();
	}

	public sealed class SalesOrderLine
	{
		public string ItemCode { get; set; } = "";
		public decimal Quantity { get; set; }
		public string WarehouseCode { get; set; } = "";
	}

	public sealed class SalesOrderCreateResponse
	{
		public int DocEntry { get; set; }
		public int DocNum { get; set; }
	}
}
