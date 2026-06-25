using EDI_Conector_FC.Models.ClientConfig;
using EDI_Conector_FC.Models.SapModels;
using EDI_Conector_FC.Services.ServiceSAP.Http;
using Microsoft.Extensions.Logging;

namespace EDI_Conector_FC.Services.CsvImporter
{
    public interface IOrderCsvImporter
    {
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
            // ── 1. Leer CSV ───────────────────────────────────────────────
            var csvOrder = _reader.ReadFromFile(csvPath);

            if (string.IsNullOrWhiteSpace(csvOrder.CardCode))
                throw new InvalidOperationException("CSV sin CardCode.");

            if (csvOrder.Lines.Count == 0)
                throw new InvalidOperationException(
                    $"CSV '{Path.GetFileName(csvPath)}' sin líneas importables.");

            // ── 2. Construir request SAP ──────────────────────────────────
            var req = new SalesOrderCreateRequest
            {
                CardCode = csvOrder.CardCode,
                DocDate = csvOrder.DocDate,
                DocDueDate = csvOrder.DocDueDate,
                NumAtCard = csvOrder.NumAtCard,
                Comments = csvOrder.Comments,

                // Campos de usuario
                U_INTRX_PR_TEMPORADA = csvOrder.Temporada,
                U_SEITemp = csvOrder.Temporada,   // mismo valor en ambos campos
                U_INTX_PR_MARCA = csvOrder.MarcaCodigo,
                U_SEIMarca = csvOrder.MarcaNumero,
                U_SEITipoPedido = csvOrder.TipoPedido,
            };

            foreach (var ln in csvOrder.Lines)
            {
                req.DocumentLines.Add(new SalesOrderLine
                {
                    ItemCode = ln.ItemCode,
                    Quantity = ln.Quantity,
                    WarehouseCode = ln.WarehouseCode,
                });
            }

            _logger.LogInformation(
                "CSV Importer: enviando {NumAtCard} — {Count} línea(s) — Tipo={Tipo} Temp={Temp} Marca={Marca}({Num})",
                csvOrder.NumAtCard, req.DocumentLines.Count,
                req.U_SEITipoPedido ?? "-",
                req.U_INTRX_PR_TEMPORADA ?? "-",
                req.U_INTX_PR_MARCA ?? "-",
                req.U_SEIMarca ?? "-");

            // ── 3. POST a SAP B1 ──────────────────────────────────────────
            var response = await _sl.PostAsync<SalesOrderCreateRequest, SalesOrderCreateResponse>(
                "Orders", req);

            if (response is null)
                throw new InvalidOperationException("SAP B1 no devolvió respuesta.");

            _logger.LogInformation(
                "SAP OK: NumAtCard={NumAtCard} DocEntry={DocEntry} DocNum={DocNum}",
                csvOrder.NumAtCard, response.DocEntry, response.DocNum);

            return response;
        }
    }
}