namespace EDI_Conector_FC.Models.Csv
{
	public sealed class CsvOrder
	{
		// ── Cabecera ──────────────────────────────────────────────────────
		public string CardCode { get; set; } = "";
		public string DocDate { get; set; } = "";
		public string DocDueDate { get; set; } = "";
		public string NumAtCard { get; set; } = "";
		public string Currency { get; set; } = "EUR";
		public string WarehouseCode { get; set; } = "";
		public string Comments { get; set; } = "";

		// ── Campos de usuario ─────────────────────────────────────────────
		public string? Temporada { get; set; }
		public string? MarcaCodigo { get; set; }
		public string? MarcaNumero { get; set; }
		public string? TipoPedido { get; set; }
		public string? GlnPuntoEntrega { get; set; }   // U_SEI_PO_EDI

		// ── Líneas ────────────────────────────────────────────────────────
		public List<CsvOrderLine> Lines { get; set; } = new();
	}

	public sealed class CsvOrderLine
	{
		public string ItemCode { get; set; } = "";
		public decimal Quantity { get; set; }
		public decimal Price { get; set; }
		public string WarehouseCode { get; set; } = "";
		public string Ean { get; set; } = "";

		/// <summary>Código del pack (@INTRX_KT_PACK) si esta línea salió de explotar un pack; vacío si es un artículo suelto.</summary>
		public string PackCode { get; set; } = "";

		/// <summary>Cantidad de packs pedidos que originaron esta línea (U_INTRX_KT_QPACK).</summary>
		public decimal PackQty { get; set; }
	}
}