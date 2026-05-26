namespace EDI_Conector_FC.Models.Csv
{
    /// <summary>
    /// Representa el CSV intermedio de un pedido.
    /// Una línea de cabecera + N líneas de detalle.
    /// </summary>
    public sealed class CsvOrder
    {
        // ── Cabecera ──────────────────────────────────────────────
        public string CardCode { get; set; } = "";
        public string DocDate { get; set; } = "";        // yyyy-MM-dd
        public string DocDueDate { get; set; } = "";     // yyyy-MM-dd
        public string NumAtCard { get; set; } = "";      // Referencia EDI del cliente
        public string Currency { get; set; } = "EUR";
        public string WarehouseCode { get; set; } = "";
        public int SlpCode { get; set; }
        public string PaymentMethod { get; set; } = "";
        public string Comments { get; set; } = "";

        // ── Líneas ────────────────────────────────────────────────
        public List<CsvOrderLine> Lines { get; set; } = new();
    }

    public sealed class CsvOrderLine
    {
        public string ItemCode { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }       // 0 si no viene en el EDI
        public string WarehouseCode { get; set; } = "";
        public string Ean { get; set; } = "";    // guardado para trazabilidad
    }
}
