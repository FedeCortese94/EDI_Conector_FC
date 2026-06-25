namespace EDI_Conector_FC.Models.SapModels
{
    public sealed class SalesOrderCreateRequest
    {
        public string CardCode { get; set; } = "";
        public string DocDate { get; set; } = "";       // yyyy-MM-dd
        public string DocDueDate { get; set; } = "";    // yyyy-MM-dd
        public string? NumAtCard { get; set; }          // referencia EDI
        public string? Comments { get; set; }           // comentario libre

        // ── Campos de usuario ─────────────────────────────────────────────
        public string? U_INTRX_PR_TEMPORADA { get; set; }   // temporada (texto)
        public string? U_SEITemp { get; set; }              // temporada (número/código SEI)
        public string? U_INTX_PR_MARCA { get; set; }        // marca código (DD, IJ, AB)
        public string? U_SEIMarca { get; set; }             // marca número (8, 17, 1)
        public string? U_SEITipoPedido { get; set; }        // tipo pedido (I / R)

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