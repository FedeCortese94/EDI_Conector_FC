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
		Task<string> GenerateAsync(EdiOrder ediOrder, ClientOptions clientOpts, CancellationToken ct);
	}

	public sealed class OrderCsvGenerator : IOrderCsvGenerator
	{
		private readonly IItemResolverService _itemResolver;
		private readonly IBooztMetadataService _metadata;
		private readonly ILogger<OrderCsvGenerator> _logger;
		private const char Tab = '\t';

		public OrderCsvGenerator(
			IItemResolverService itemResolver,
			IBooztMetadataService metadata,
			ILogger<OrderCsvGenerator> logger)
		{
			_itemResolver = itemResolver;
			_metadata = metadata;
			_logger = logger;
		}

		public async Task<string> GenerateAsync(EdiOrder ediOrder, ClientOptions clientOpts, CancellationToken ct)
		{
			var sap = clientOpts.SapDefaults;
			var paths = clientOpts.Paths;

			// ── 1. Resolver EANs → ItemCodes ─────────────────────────────
			int resolved = 0, notFound = 0;
			var csvLines = new List<CsvOrderLine>();

			foreach (var ln in ediOrder.Lines)
			{
				ct.ThrowIfCancellationRequested();

				var ean = PadEan(ln.Ean, clientOpts.Parser.EanPadToLength);
				var itemCode = await _itemResolver.ResolveItemCodeFromEanAsync(ean, ct);

				if (string.IsNullOrWhiteSpace(itemCode))
				{
					_logger.LogWarning(
						"EAN sin ItemCode. Cliente={ClientId} EdiDoc={Doc} Línea={N} EAN={Ean}",
						clientOpts.ClientId, ediOrder.EdiDocNum, ln.LineNo, ean);
					notFound++;
					csvLines.Add(new CsvOrderLine
					{
						ItemCode = $"[NOT_FOUND:{ean}]",
						Quantity = ln.Quantity,
						Price = 0m,
						WarehouseCode = sap.WarehouseCode,
						Ean = ean,
					});
					continue;
				}

				csvLines.Add(new CsvOrderLine
				{
					ItemCode = itemCode,
					Quantity = ln.Quantity,
					Price = 0m,
					WarehouseCode = sap.WarehouseCode,
					Ean = ean,
				});
				resolved++;
			}

			_logger.LogInformation(
				"CSV Generator: EdiDoc={Doc} Líneas={Total} Resueltas={Ok} NoEncontradas={Nf}",
				ediOrder.EdiDocNum, ediOrder.Lines.Count, resolved, notFound);

			// ── 2. Resolver metadatos ─────────────────────────────────────
			var tipoPedido = ediOrder.TipoDoc == "224" ? "R" : "I";

			var itemCodes = csvLines
				.Where(l => !l.ItemCode.StartsWith("[NOT_FOUND:"))
				.Select(l => l.ItemCode).ToList();

			var temporada = await _metadata.GetTemporadaAsync(ediOrder.TipoDoc, itemCodes, ct);
			var (marcaCodigo, marcaNumero) = itemCodes.Count > 0
				? await _metadata.GetMarcaAsync(itemCodes[0], ct)
				: (null, null);

			_logger.LogInformation(
				"Metadatos: TipoPedido={Tipo} Temporada={Temp} Marca={Marca}({Num}) GLN_DP={Gln}",
				tipoPedido, temporada ?? "null", marcaCodigo ?? "null",
				marcaNumero ?? "null", ediOrder.GlnPuntoEntrega ?? "null");

			// ── 3. Construir CsvOrder ─────────────────────────────────────
			var csvOrder = new CsvOrder
			{
				CardCode = sap.CardCode,
				DocDate = ediOrder.DocDate.ToString("dd/MM/yyyy"),
				DocDueDate = ediOrder.DocDueDate.ToString("dd/MM/yyyy"),
				NumAtCard = ediOrder.EdiDocNum,
				Currency = sap.Currency,
				WarehouseCode = sap.WarehouseCode,
				Comments = $"{sap.CommentsPrefix} {ediOrder.EdiDocNum}".Trim(),
				Temporada = temporada,
				MarcaCodigo = marcaCodigo,
				MarcaNumero = marcaNumero,
				TipoPedido = tipoPedido,
				GlnPuntoEntrega = ediOrder.GlnPuntoEntrega,
				Lines = csvLines,
			};

			// ── 4. Escribir CSV ───────────────────────────────────────────
			Directory.CreateDirectory(paths.OrdersCsv);
			var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			var fileName = $"{clientOpts.ClientId}_{ediOrder.EdiDocNum}_{stamp}.csv";
			var filePath = Path.Combine(paths.OrdersCsv, fileName);
			await WriteCsvAsync(filePath, csvOrder);
			_logger.LogInformation("CSV generado: {FilePath}", filePath);
			return filePath;
		}

		private static async Task WriteCsvAsync(string path, CsvOrder order)
		{
			var sb = new StringBuilder();

			// Cabecera: ...| TipoPedido | Temporada | MarcaCodigo | MarcaNumero | GlnPuntoEntrega
			sb.AppendLine(string.Join(Tab,
				order.CardCode, order.DocDate, order.DocDueDate, order.NumAtCard,
				order.Currency, order.WarehouseCode, order.Comments,
				order.TipoPedido ?? "",
				order.Temporada ?? "",
				order.MarcaCodigo ?? "",
				order.MarcaNumero ?? "",
				order.GlnPuntoEntrega ?? ""));

			foreach (var ln in order.Lines)
			{
				sb.AppendLine(string.Join(Tab,
					ln.ItemCode,
					ln.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
					ln.Price.ToString("0.##", CultureInfo.InvariantCulture),
					ln.WarehouseCode,
					ln.Ean));
			}

			await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
		}

		private static string PadEan(string ean, int targetLength)
		{
			if (targetLength <= 0 || ean.Length >= targetLength) return ean;
			return ean.PadLeft(targetLength, '0');
		}
	}
}