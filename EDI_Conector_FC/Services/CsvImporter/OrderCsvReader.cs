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
        private const char Sep = ';';

        public OrderCsvReader(ILogger<OrderCsvReader> logger)
        {
            _logger = logger;
        }

        public CsvOrder ReadFromFile(string csvPath)
        {
            var lines = File.ReadAllLines(csvPath)
                            .Select(l => l.Trim())
                            .ToList();

            var order = new CsvOrder();
            var section = "";

            // Índices de columnas (se resuelven al leer la línea de cabeceras)
            var headerIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var linesIdx  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            bool headerDataRead = false;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                // Ignorar comentarios y líneas vacías
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("##"))
                    continue;

                // Detectar sección
                if (line.Equals("HEADER", StringComparison.OrdinalIgnoreCase))
                {
                    section = "HEADER";
                    continue;
                }
                if (line.Equals("LINES", StringComparison.OrdinalIgnoreCase))
                {
                    section = "LINES";
                    continue;
                }

                var fields = SplitCsvLine(line);

                if (section == "HEADER")
                {
                    if (headerIdx.Count == 0)
                    {
                        // Primera línea de HEADER: nombres de columnas
                        for (int c = 0; c < fields.Count; c++)
                            headerIdx[fields[c]] = c;
                        continue;
                    }

                    if (!headerDataRead)
                    {
                        // Segunda línea de HEADER: datos
                        order.CardCode      = Get(fields, headerIdx, "CardCode");
                        order.DocDate       = Get(fields, headerIdx, "DocDate");
                        order.DocDueDate    = Get(fields, headerIdx, "DocDueDate");
                        order.NumAtCard     = Get(fields, headerIdx, "NumAtCard");
                        order.Currency      = Get(fields, headerIdx, "Currency");
                        order.WarehouseCode = Get(fields, headerIdx, "WarehouseCode");
                        order.PaymentMethod = Get(fields, headerIdx, "PaymentMethod");
                        order.Comments      = Get(fields, headerIdx, "Comments");

                        if (int.TryParse(Get(fields, headerIdx, "SlpCode"), out var slp))
                            order.SlpCode = slp;

                        headerDataRead = true;
                    }
                }
                else if (section == "LINES")
                {
                    if (linesIdx.Count == 0)
                    {
                        // Primera línea de LINES: nombres de columnas
                        for (int c = 0; c < fields.Count; c++)
                            linesIdx[fields[c]] = c;
                        continue;
                    }

                    // Saltamos líneas con ItemCode vacío o marcadas como no encontradas
                    var itemCode = Get(fields, linesIdx, "ItemCode");
                    if (string.IsNullOrWhiteSpace(itemCode) || itemCode.StartsWith("[NOT_FOUND:"))
                    {
                        _logger.LogWarning("CSV Línea omitida (ItemCode no resuelto): {Line}", line);
                        continue;
                    }

                    var csvLine = new CsvOrderLine
                    {
                        ItemCode      = itemCode,
                        WarehouseCode = Get(fields, linesIdx, "WarehouseCode"),
                        Ean           = Get(fields, linesIdx, "EAN"),
                    };

                    if (decimal.TryParse(Get(fields, linesIdx, "Quantity"),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out var qty))
                        csvLine.Quantity = qty;

                    if (decimal.TryParse(Get(fields, linesIdx, "Price"),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                        csvLine.Price = price;

                    order.Lines.Add(csvLine);
                }
            }

            if (!headerDataRead)
                throw new InvalidDataException($"El CSV '{csvPath}' no contiene una sección HEADER válida.");

            if (order.Lines.Count == 0)
                throw new InvalidDataException($"El CSV '{csvPath}' no contiene líneas de detalle importables.");

            _logger.LogInformation("CSV leído: {NumAtCard} — {Count} línea(s)", order.NumAtCard, order.Lines.Count);

            return order;
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────

        private static string Get(
            List<string> fields,
            Dictionary<string, int> idx,
            string column)
        {
            if (!idx.TryGetValue(column, out var i)) return "";
            if (i >= fields.Count) return "";
            return fields[i];
        }

        /// <summary>
        /// Split de línea CSV respetando campos entre comillas dobles.
        /// </summary>
        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // saltar la segunda comilla
                    }
                    else if (c == '"')
                    {
                        inQuotes = false;
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ';')
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }

            result.Add(current.ToString());
            return result;
        }
    }
}
