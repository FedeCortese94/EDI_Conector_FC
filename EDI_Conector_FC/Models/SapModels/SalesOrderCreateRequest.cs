namespace EDI_Conector_FC.Models.SapModels
{
	public sealed class SalesOrderCreateRequest
	{
		public string CardCode { get; set; } = "";
		public string DocDate { get; set; } = "";
		public string DocDueDate { get; set; } = "";
		public string? NumAtCard { get; set; }
		public string? Comments { get; set; }

		// ── Campos de usuario — pedido ────────────────────────────────────
		public string? U_INTRX_PR_TEMPORADA { get; set; }
		public string? U_SEITemp { get; set; }
		public string? U_INTRX_PR_MARCA { get; set; }
		public string? U_SEIMARCA { get; set; }
		public string? U_SEITipoPedido { get; set; }

		/// <summary>GLN del punto de entrega — leído del ERE1P DP del fichero EDI.</summary>
		public string? U_SEI_PO_EDI { get; set; }

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