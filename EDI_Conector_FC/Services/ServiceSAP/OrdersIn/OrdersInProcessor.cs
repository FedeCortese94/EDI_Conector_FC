using EDI_Conector_FC.Models;
using EDI_Conector_FC.Services.ServiceSAP;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using EDI_Conector_FC.Models.SapModels;
using EDI_Conector_FC.Services.ServiceSAP.Http;
using EDI_Conector_FC.Services.Remote;


namespace EDI_Conector_FC.Services.OrdersIn
{
	public interface IOrdersInProcessor
	{
		Task ProcessAsync(CancellationToken ct);
	}

	public sealed class OrdersInProcessor : IOrdersInProcessor
	{
		private readonly ILogger<OrdersInProcessor> _logger;
		private readonly IOptions<PathsOptions> _paths;
		private readonly IEre1OrdersParser _parser;
		private readonly IItemResolverService _itemResolver;

		private readonly IOptions<OrdersInOptions> _ordersOpt;
		private readonly ServiceLayerClient _sl;

		private readonly IFtpService _ftp;

		public OrdersInProcessor(
			ILogger<OrdersInProcessor> logger,
			IOptions<PathsOptions> paths,
			IEre1OrdersParser parser,
			IItemResolverService itemResolver,
			IOptions<OrdersInOptions> ordersOpt,
			ServiceLayerClient sl,
			IFtpService ftp)

		{
			_logger = logger;
			_paths = paths;
			_parser = parser;
			_itemResolver = itemResolver;
			_ordersOpt = ordersOpt;
			_sl = sl;
			_ftp = ftp;
		}

		public async Task ProcessAsync(CancellationToken ct)
		{
			var inbox = RequiredPath(_paths.Value.OrdersInbox, nameof(_paths.Value.OrdersInbox));
			var processed = RequiredPath(_paths.Value.OrdersProcessed, nameof(_paths.Value.OrdersProcessed));
			var error = RequiredPath(_paths.Value.OrdersError, nameof(_paths.Value.OrdersError));

			Directory.CreateDirectory(inbox);
			Directory.CreateDirectory(processed);
			Directory.CreateDirectory(error);


			// 0) Descargar del FTP a Inbox
			var remoteFiles = await _ftp.ListAsync(ct);

			foreach (var rf in remoteFiles)
			{
				ct.ThrowIfCancellationRequested();

				var localPath = Path.Combine(inbox, rf.Name);

				// Evitar re-descarga si ya está (opcional)
				if (File.Exists(localPath))
					continue;

				await _ftp.DownloadAsync(rf.Name, localPath, ct);

				// Opcional (productivo): borrar del FTP para que no se repita
				// OJO: si quieres esperar a que SAP cree OK, entonces NO borres aquí.
				// await _ftp.DeleteAsync(rf.Name, ct);
			}



			var files = Directory.EnumerateFiles(inbox)
								 .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
								 .ToList();

			if (files.Count == 0)
			{
				_logger.LogInformation("Orders IN: inbox vacío ({Inbox})", inbox);
				return;
			}

			_logger.LogInformation("Orders IN: {Count} fichero(s) encontrados en {Inbox}", files.Count, inbox);

			foreach (var file in files)
			{
				ct.ThrowIfCancellationRequested();

				_logger.LogInformation("Orders IN: procesando {File}", Path.GetFileName(file));

				try
				{
					//Parse, busca articulos y crea el pedido en SAP.


					var ediOrder = _parser.ParseFile(file);

					if (ediOrder.Lines.Count == 0)
						throw new InvalidOperationException($"Pedido {ediOrder.EdiDocNum} sin líneas ERE1L.");


					_logger.LogInformation("Orders IN: OK parse. EdiDocNum={EdiDocNum} Lines={Lines}",
						ediOrder.EdiDocNum, ediOrder.Lines.Count);


					//Crear pedido en SAP
					///////////////////
					var cardCode = _ordersOpt.Value.CardCode;
					var whsCode = _ordersOpt.Value.WhsCode;

					if (string.IsNullOrWhiteSpace(cardCode))
						throw new InvalidOperationException("OrdersIn: CardCode vacío en appsettings.");
					if (string.IsNullOrWhiteSpace(whsCode))
						throw new InvalidOperationException("OrdersIn: WhsCode vacío en appsettings.");

					var req = new SalesOrderCreateRequest
					{
						CardCode = cardCode,
						DocDate = ediOrder.DocDate.ToString("yyyy-MM-dd"),
						DocDueDate = ediOrder.DocDueDate.ToString("yyyy-MM-dd"),
						NumAtCard = ediOrder.EdiDocNum, // referencia EDI (útil)
					};

					foreach (var ln in ediOrder.Lines)
					{
						var itemCode = await _itemResolver.ResolveItemCodeFromEanAsync(ln.Ean, ct);
						if (string.IsNullOrWhiteSpace(itemCode))
							throw new InvalidOperationException($"EAN sin correspondencia en SAP: {ln.Ean} (línea {ln.LineNo})");

						req.DocumentLines.Add(new SalesOrderLine
						{
							ItemCode = itemCode,
							Quantity = ln.Quantity,
							WarehouseCode = whsCode
						});
					}

					// POST /Orders
					var created = await _sl.PostAsync<SalesOrderCreateRequest, SalesOrderCreateResponse>("Orders", req);

					_logger.LogInformation("Orders IN: Pedido creado en SAP. EdiDocNum={EdiDocNum} DocEntry={DocEntry} DocNum={DocNum}",
						ediOrder.EdiDocNum, created?.DocEntry, created?.DocNum);
					///////////////////

					//Move OK
					MoveToFolder(file, processed, ".OK");
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Orders IN: ERROR procesando {File}", Path.GetFileName(file));

					try
					{
						MoveToFolder(file, error, ".ERR");
					}
					catch (Exception moveEx)
					{
						_logger.LogError(moveEx, "Orders IN: no se pudo mover a Error el fichero {File}", Path.GetFileName(file));
					}
				}
			}
		}

		private static string RequiredPath(string? path, string name)
			=> string.IsNullOrWhiteSpace(path) ? throw new InvalidOperationException($"PathsOptions: {name} está vacío.") : path;

		private static void MoveToFolder(string sourceFile, string destFolder, string suffix)
		{
			var name = Path.GetFileName(sourceFile);
			var destFile = Path.Combine(destFolder, $"{name}{suffix}");

			if (File.Exists(destFile))
			{
				var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
				destFile = Path.Combine(destFolder, $"{name}{suffix}.{stamp}");
			}

			File.Move(sourceFile, destFile);
		}
	}
}
