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

		/// <summary>Número de bulto físico real (caja). Líneas con el mismo valor van en la misma caja/SSCC.
		/// Reemplazado por U_FC_Bultos/U_FC_QBultos; se mantiene como fallback si esos no vienen.</summary>
		public decimal? U_SEI_NumBulto { get; set; }

		/// <summary>Números de bulto en los que se reparte esta línea, separados por ";" (ej. "15741;15742").</summary>
		public string? U_FC_Bultos { get; set; }

		/// <summary>Cantidad correspondiente a cada bulto de U_FC_Bultos, en el mismo orden, separadas por ";" (ej. "1;2").</summary>
		public string? U_FC_QBultos { get; set; }
	}

	/// <summary>Fila de la tabla interna de bultos @SEI_LINEAS_PACKINGL (picking/packing).</summary>
	public sealed class SlPackingLine
	{
		public string? Code { get; set; }
		public string? U_DocEntry { get; set; }
		public int U_Linea { get; set; }
		public string? U_ItemCode { get; set; }
		public int U_NBulto { get; set; }
		public decimal U_Cantidad { get; set; }
	}

	public sealed class SlPackingListResponse
	{
		public List<SlPackingLine> Value { get; set; } = new();

		[System.Text.Json.Serialization.JsonPropertyName("odata.nextLink")]
		public string? NextLink { get; set; }
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