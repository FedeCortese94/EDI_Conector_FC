using EDI_Conector_FC.Models.ClientConfig;
using EDI_Conector_FC.Models.Csv;
using EDI_Conector_FC.Services.OrdersIn;
using EDI_Conector_FC.Services.ServiceSAP;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace EDI_Conector_FC.Services.CsvGenerator
{
    public interface IOrderCsvGenerator
    {
        /// <summary>
        /// Convierte un EdiOrder parseado en un fichero CSV intermedio.
        /// Devuelve la ruta del fichero CSV generado.
        /// </summary>
        Task<string> GenerateAsync(
            EdiOrder ediOrder,
            ClientOptions clientOpts,
            CancellationToken ct);
    }

    public sealed class OrderCsvGenerator : IOrderCsvGenerator
    {
        private readonly IItemResolverService _itemResolver;
        private readonly ILogger<OrderCsvGenerator> _logger;

        // Separador del CSV
        private const char Sep = ';';

        public OrderCsvGenerator(
            IItemResolverService itemResolver,
            ILogger<OrderCsvGenerator> logger)
        {
            _itemResolver = itemResolver;
            _logger = logger;
        }

        public async Task<string> GenerateAsync(
            EdiOrder ediOrder,
            ClientOptions clientOpts,
            CancellationToken ct)
        {
            var sap = clientOpts.SapDefaults;
            var paths = clientOpts.Paths;

            // ── 1. Construir el modelo CSV ────────────────────────────────
            var csvOrder = new CsvOrder
            {
                CardCode     = sap.CardCode,
                DocDate      = ediOrder.DocDate.ToString("yyyy-MM-dd"),
                DocDueDate   = ediOrder.DocDueDate.ToString("yyyy-MM-dd"),
                NumAtCard    = ediOrder.EdiDocNum,
                Currency     = sap.Currency,
                WarehouseCode = sap.WarehouseCode,
                SlpCode      = sap.SlpCode,
                PaymentMethod = sap.PaymentMethod,
                Comments     = $"{sap.CommentsPrefix} {clientOpts.ClientId} {ediOrder.EdiDocNum}".Trim(),
            };

            // ── 2. Resolver EANs → ItemCodes ─────────────────────────────
            int resolved = 0, notFound = 0;

            foreach (var ln in ediOrder.Lines)
            {
                ct.ThrowIfCancellationRequested();

                var ean = PadEan(ln.Ean, clientOpts.Parser.EanPadToLength);
                var itemCode = await _itemResolver.ResolveItemCodeFromEanAsync(ean, ct);

                if (string.IsNullOrWhiteSpace(itemCode))
                {
                    _logger.LogWarning(
                        "EAN sin ItemCode en SAP. Cliente={ClientId} EdiDoc={EdiDoc} Línea={LineNo} EAN={Ean}",
                        clientOpts.ClientId, ediOrder.EdiDocNum, ln.LineNo, ean);

                    notFound++;

                    // Guardamos la línea igualmente en el CSV con ItemCode vacío
                    // para que el revisor vea qué artículos faltan.
                    csvOrder.Lines.Add(new CsvOrderLine
                    {
                        ItemCode      = $"[NOT_FOUND:{ean}]",
                        Quantity      = ln.Quantity,
                        Price         = 0m,
                        WarehouseCode = sap.WarehouseCode,
                        Ean           = ean,
                    });

                    continue;
                }

                csvOrder.Lines.Add(new CsvOrderLine
                {
                    ItemCode      = itemCode,
                    Quantity      = ln.Quantity,
                    Price         = 0m,       // Boozt no manda precio; SAP usa lista de precios
                    WarehouseCode = sap.WarehouseCode,
                    Ean           = ean,
                });

                resolved++;
            }

            _logger.LogInformation(
                "CSV Generator: EdiDoc={EdiDoc} Líneas={Total} Resueltas={Ok} NoEncontradas={Nf}",
                ediOrder.EdiDocNum, ediOrder.Lines.Count, resolved, notFound);

            // ── 3. Escribir el fichero CSV ────────────────────────────────
            Directory.CreateDirectory(paths.OrdersCsv);

            // Nombre: {ClientId}_{EdiDocNum}_{timestamp}.csv
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{clientOpts.ClientId}_{ediOrder.EdiDocNum}_{stamp}.csv";
            var filePath = Path.Combine(paths.OrdersCsv, fileName);

            await WriteCsvAsync(filePath, csvOrder);

            _logger.LogInformation("CSV generado: {FilePath}", filePath);

            return filePath;
        }

        // ─────────────────────────────────────────────────────────────────
        // Escritura del CSV
        // ─────────────────────────────────────────────────────────────────

        private static async Task WriteCsvAsync(string path, CsvOrder order)
        {
            var sb = new StringBuilder();

            // -- Cabecera del fichero (metadatos del pedido)
            sb.AppendLine("## EDI_Conector_FC - Pedido intermedio CSV");
            sb.AppendLine($"## Generado: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"## NumAtCard: {order.NumAtCard}");
            sb.AppendLine();

            // -- Cabecera del pedido (una sola línea)
            sb.AppendLine("HEADER");
            sb.AppendLine(string.Join(Sep,
                "CardCode", "DocDate", "DocDueDate", "NumAtCard",
                "Currency", "WarehouseCode", "SlpCode", "PaymentMethod", "Comments"));

            sb.AppendLine(string.Join(Sep,
                Escape(order.CardCode),
                Escape(order.DocDate),
                Escape(order.DocDueDate),
                Escape(order.NumAtCard),
                Escape(order.Currency),
                Escape(order.WarehouseCode),
                order.SlpCode.ToString(CultureInfo.InvariantCulture),
                Escape(order.PaymentMethod),
                Escape(order.Comments)));

            sb.AppendLine();

            // -- Líneas de detalle
            sb.AppendLine("LINES");
            sb.AppendLine(string.Join(Sep,
                "ItemCode", "Quantity", "Price", "WarehouseCode", "EAN"));

            foreach (var ln in order.Lines)
            {
                sb.AppendLine(string.Join(Sep,
                    Escape(ln.ItemCode),
                    ln.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
                    ln.Price.ToString("0.##", CultureInfo.InvariantCulture),
                    Escape(ln.WarehouseCode),
                    Escape(ln.Ean)));
            }

            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
        }

        // ─────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rellena el EAN por la izquierda con ceros hasta la longitud objetivo.
        /// Si targetLength = 0 devuelve el EAN tal cual.
        /// </summary>
        private static string PadEan(string ean, int targetLength)
        {
            if (targetLength <= 0 || ean.Length >= targetLength)
                return ean;

            return ean.PadLeft(targetLength, '0');
        }

        /// <summary>
        /// Escapa un campo CSV: si contiene el separador o comillas, lo envuelve en comillas dobles.
        /// </summary>
        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(Sep) || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }
    }
}
