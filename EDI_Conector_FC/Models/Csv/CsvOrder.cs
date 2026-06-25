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
        public string? TipoPedido { get; set; }     // "I" o "R"

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
    }
}