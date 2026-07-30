namespace EDI_Conector_FC.Models.Desadv
{
	public sealed class DesadvAlbaran
	{
		public int DocEntry { get; set; }
		public string DocNum { get; set; } = "";
		public string NumAtCard { get; set; } = "";
		public string DocDate { get; set; } = "";
		public string DocDueDate { get; set; } = "";

		/// <summary>GLN comprador e facturado (BY/IV) — de OCRD.U_SEIPOEDI</summary>
		public string GlnCliente { get; set; } = "";

		/// <summary>GLN punto de entrega (DP) — de U_SEI_PO_EDI en cascada</summary>
		public string GlnPuntoEntrega { get; set; } = "";

		public List<DesadvLinea> Lineas { get; set; } = new();

		/// <summary>DocNum del pedido de venta SAP origen.</summary>
		public string DocNumPedido { get; set; } = "";
	}

	public sealed class DesadvLinea
	{
		public int LineNum { get; set; }
		public int Bulto { get; set; }
		public string ItemCode { get; set; } = "";
		public string Descripcion { get; set; } = "";
		public string Ean { get; set; } = "";
		public decimal Cantidad { get; set; }
		public string Sscc { get; set; } = "";
	}
}