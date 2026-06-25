using EDI_Conector_FC.Models.Csv;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace EDI_Conector_FC.Services.CsvImporter
{
    public interface IOrderCsvReader
    {
        CsvOrder ReadFromFile(string csvPath);
    }

    public sealed class OrderCsvReader : IOrderCsvReader
    {
        private readonly ILogger<OrderCsvReader> _logger;

        public OrderCsvReader(ILogger<OrderCsvReader> logger)
        {
            _logger = logger;
        }

        public CsvOrder ReadFromFile(string csvPath)
        {
            var lines = File.ReadAllLines(csvPath)
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .ToList();

            if (lines.Count < 2)
                throw new InvalidDataException(
                    $"El CSV '{Path.GetFileName(csvPath)}' debe tener cabecera + al menos 1 línea.");

            var order = new CsvOrder();

            // ── Línea 0: cabecera del pedido ─────────────────────────────
            // CardCode | DocDate | DocDueDate | NumAtCard | Currency | WhsCode | Comments | TipoPedido | Temporada | MarcaCodigo | MarcaNumero
            var header = Split(lines[0]);

            order.CardCode = Col(header, 0);
            order.DocDate = ParseDate(Col(header, 1));
            order.DocDueDate = ParseDate(Col(header, 2));
            order.NumAtCard = Col(header, 3);
            order.Currency = Col(header, 4);
            order.WarehouseCode = Col(header, 5);
            order.Comments = Col(header, 6);
            order.TipoPedido = Col(header, 7);
            order.Temporada = Col(header, 8);
            order.MarcaCodigo = Col(header, 9);
            order.MarcaNumero = Col(header, 10);

            if (string.IsNullOrWhiteSpace(order.CardCode))
                throw new InvalidDataException(
                    $"El CSV '{Path.GetFileName(csvPath)}' no tiene CardCode.");

            // ── Líneas 1..N: detalle ──────────────────────────────────────
            // ItemCode | Quantity | Price | WarehouseCode | EAN
            for (int i = 1; i < lines.Count; i++)
            {
                var cols = Split(lines[i]);
                var itemCode = Col(cols, 0);

                if (string.IsNullOrWhiteSpace(itemCode)) continue;

                if (itemCode.StartsWith("[NOT_FOUND:"))
                {
                    _logger.LogWarning("Línea {N} omitida (ItemCode no resuelto): {ItemCode}", i, itemCode);
                    continue;
                }

                var line = new CsvOrderLine
                {
                    ItemCode = itemCode,
                    WarehouseCode = Col(cols, 3),
                    Ean = Col(cols, 4),
                };

                if (decimal.TryParse(Col(cols, 1), NumberStyles.Any, CultureInfo.InvariantCulture, out var qty))
                    line.Quantity = qty;

                if (decimal.TryParse(Col(cols, 2), NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                    line.Price = price;

                order.Lines.Add(line);
            }

            if (order.Lines.Count == 0)
                throw new InvalidDataException(
                    $"El CSV '{Path.GetFileName(csvPath)}' no tiene líneas importables.");

            _logger.LogInformation(
                "CSV leído: {NumAtCard} — {Count} línea(s) Tipo={Tipo} Temp={Temp} Marca={Marca}",
                order.NumAtCard, order.Lines.Count,
                order.TipoPedido ?? "-", order.Temporada ?? "-", order.MarcaCodigo ?? "-");

            return order;
        }

        private static List<string> Split(string line)
            => line.Split('\t').Select(c => c.Trim()).ToList();

        private static string Col(List<string> cols, int idx)
            => idx < cols.Count ? cols[idx] : "";

        private static string ParseDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            if (DateTime.TryParseExact(raw, "dd/MM/yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1))
                return d1.ToString("yyyy-MM-dd");

            if (DateTime.TryParseExact(raw, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
                return d2.ToString("yyyy-MM-dd");

            return raw;
        }
    }
}