using EDI_Conector_FC.Models.ClientConfig;
using EDI_Conector_FC.Models.SapModels;
using EDI_Conector_FC.Services.ServiceSAP.Http;
using Microsoft.Extensions.Logging;

namespace EDI_Conector_FC.Services.CsvImporter
{
    public interface IOrderCsvImporter
    {
        /// <summary>
        /// Lee un CSV intermedio y crea el pedido de venta en SAP B1.
        /// Devuelve el DocEntry creado.
        /// </summary>
        Task<SalesOrderCreateResponse> ImportAsync(
            string csvPath,
            ClientOptions clientOpts,
            CancellationToken ct);
    }

    public sealed class OrderCsvImporter : IOrderCsvImporter
    {
        private readonly IOrderCsvReader _reader;
        private readonly ServiceLayerClient _sl;
        private readonly ILogger<OrderCsvImporter> _logger;

        public OrderCsvImporter(
            IOrderCsvReader reader,
            ServiceLayerClient sl,
            ILogger<OrderCsvImporter> logger)
        {
            _reader = reader;
            _sl = sl;
            _logger = logger;
        }

        public async Task<SalesOrderCreateResponse> ImportAsync(
            string csvPath,
            ClientOptions clientOpts,
            CancellationToken ct)
        {
            // ── 1. Leer el CSV ───────────────────────────────────────────
            var csvOrder = _reader.ReadFromFile(csvPath);

            // ── 2. Validaciones básicas ──────────────────────────────────
            if (string.IsNullOrWhiteSpace(csvOrder.CardCode))
                throw new InvalidOperationException("CSV sin CardCode.");

            if (csvOrder.Lines.Count == 0)
                throw new InvalidOperationException($"CSV '{Path.GetFileName(csvPath)}' sin líneas importables.");

            // ── 3. Construir el request para SAP ─────────────────────────
            var req = new SalesOrderCreateRequest
            {
                CardCode   = csvOrder.CardCode,
                DocDate    = csvOrder.DocDate,
                DocDueDate = csvOrder.DocDueDate,
                NumAtCard  = csvOrder.NumAtCard,
            };

            foreach (var ln in csvOrder.Lines)
            {
                req.DocumentLines.Add(new SalesOrderLine
                {
                    ItemCode      = ln.ItemCode,
                    Quantity      = ln.Quantity,
                    WarehouseCode = ln.WarehouseCode,
                });
            }

            _logger.LogInformation(
                "CSV Importer: enviando pedido {NumAtCard} — {Count} línea(s) — Cliente={CardCode}",
                csvOrder.NumAtCard, req.DocumentLines.Count, req.CardCode);

            // ── 4. POST a SAP B1 Service Layer ───────────────────────────
            var response = await _sl.PostAsync<SalesOrderCreateRequest, SalesOrderCreateResponse>(
                "Orders", req);

            if (response is null)
                throw new InvalidOperationException("SAP B1 no devolvió respuesta al crear el pedido.");

            _logger.LogInformation(
                "CSV Importer: Pedido creado en SAP. NumAtCard={NumAtCard} DocEntry={DocEntry} DocNum={DocNum}",
                csvOrder.NumAtCard, response.DocEntry, response.DocNum);

            return response;
        }
    }
}
