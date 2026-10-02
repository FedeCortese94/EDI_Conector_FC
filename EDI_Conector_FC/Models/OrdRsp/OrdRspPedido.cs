namespace EDI_Conector_FC.Models.OrdRsp
{
	public sealed class OrdRspPedido
	{
		public int DocEntry { get; set; }
		public string DocNum { get; set; } = "";
		public string NumAtCard { get; set; } = "";
		public string DocDate { get; set; } = "";
		public string DocDueDate { get; set; } = "";

		/// <summary>GLN comprador — de OCRD.U_SEIPOEDI</summary>
		public string GlnCliente { get; set; } = "";

		/// <summary>GLN punto de entrega — de U_SEI_PO_EDI del pedido.</summary>
		public string GlnPuntoEntrega { get; set; } = "";

		public List<OrdRspLinea> Lineas { get; set; } = new();
	}

	public sealed class OrdRspLinea
	{
		public int LineNum { get; set; }
		public string ItemCode { get; set; } = "";
		public string Ean { get; set; } = "";
		public decimal Cantidad { get; set; }

		/// <summary>Precio confirmado en SAP (el que realmente quedó en la línea del pedido).</summary>
		public decimal Precio { get; set; }
	}
}
