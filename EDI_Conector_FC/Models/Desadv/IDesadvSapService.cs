using EDI_Conector_FC.Models;
using EDI_Conector_FC.Models.ClientConfig;
using EDI_Conector_FC.Models.Desadv;
using EDI_Conector_FC.Services.ServiceSAP;
using EDI_Conector_FC.Services.ServiceSAP.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EDI_Conector_FC.Services.Desadv
{
	public interface IDesadvSapService
	{
		Task<List<DesadvAlbaran>> GetPendingAlbaranesAsync(ClientOptions client, CancellationToken ct);
		Task MarkAsSentAsync(int docEntry, CancellationToken ct);
	}

	public sealed class DesadvSapService : IDesadvSapService
	{
		private readonly ServiceLayerClient _sl;
		private readonly IPackResolverService _packResolver;
		private readonly IOptions<PacksOptions> _packsOptions;
		private readonly ILogger<DesadvSapService> _logger;

		public DesadvSapService(
			ServiceLayerClient sl,
			IPackResolverService packResolver,
			IOptions<PacksOptions> packsOptions,
			ILogger<DesadvSapService> logger)
		{
			_sl = sl;
			_packResolver = packResolver;
			_packsOptions = packsOptions;
			_logger = logger;
		}

		public async Task<List<DesadvAlbaran>> GetPendingAlbaranesAsync(
			ClientOptions client, CancellationToken ct)
		{
			var cardCode = client.SapDefaults.CardCode;
			var result = new List<DesadvAlbaran>();

			var filter = Uri.EscapeDataString(
				$"Cancelled eq 'tNO' and CardCode eq '{cardCode}' and " +
				$"(U_SEI_ENVIOMAIL eq '' or U_SEI_ENVIOMAIL eq null or U_SEI_ENVIOMAIL eq '3')");

			var select = "DocEntry,DocNum,NumAtCard,DocDate,DocDueDate,CardCode,DocumentLines,U_SEI_PO_EDI";
			var url = $"DeliveryNotes?$filter={filter}&$select={select}";

			var response = await _sl.GetAsync<SlListResponse<SlDeliveryNote>>(url, ct);

			if (response?.Value == null || response.Value.Count == 0)
			{
				_logger.LogInformation("DESADV: no hay albaranes pendientes para {CardCode}", cardCode);
				return result;
			}

			_logger.LogInformation("DESADV: {Count} albarán(es) pendiente(s) para {CardCode}",
				response.Value.Count, cardCode);

			// GLN del cliente (BY e IV — fijo del BP)
			var glnCliente = await GetGlnClienteAsync(cardCode, ct);

			foreach (var dn in response.Value)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					var albaran = await BuildAlbaranAsync(dn, glnCliente ?? "", ct);
					if (albaran != null) result.Add(albaran);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "DESADV: error procesando albarán DocNum={DocNum}", dn.DocNum);
				}
			}

			return result;
		}

		public async Task MarkAsSentAsync(int docEntry, CancellationToken ct)
		{
			await _sl.PatchAsync($"DeliveryNotes({docEntry})", new { U_SEI_ENVIOMAIL = "1" });
			_logger.LogInformation("DESADV: albarán DocEntry={DocEntry} marcado como enviado.", docEntry);
		}

		// ─────────────────────────────────────────────────────────────────

		private async Task<string?> GetGlnClienteAsync(string cardCode, CancellationToken ct)
		{
			try
			{
				var escaped = cardCode.Replace("'", "''");
				var bp = await _sl.GetAsync<SlBusinessPartner>(
					$"BusinessPartners('{escaped}')?$select=U_SEIPOEDI", ct);
				return bp?.U_SEIPOEDI?.Trim();
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "DESADV: error leyendo U_SEIPOEDI del cliente {CardCode}", cardCode);
				return null;
			}
		}

		/// <summary>
		/// Obtiene el GLN del punto de entrega en cascada:
		/// 1. U_SEI_PO_EDI del albarán
		/// 2. U_SEI_PO_EDI del pedido origen (BaseEntry de la primera línea)
		/// 3. U_SEIPOEDI del BP como fallback
		/// </summary>
		private async Task<string?> GetGlnPuntoEntregaAsync(
			SlDeliveryNote dn, string glnClienteFallback, CancellationToken ct)
		{
			// 1. Del albarán directamente
			if (!string.IsNullOrWhiteSpace(dn.U_SEI_PO_EDI))
				return dn.U_SEI_PO_EDI.Trim();

			// 2. Del pedido origen (BaseEntry de la primera línea)
			var baseEntry = dn.DocumentLines?.FirstOrDefault()?.BaseEntry;
			if (baseEntry.HasValue && baseEntry.Value > 0)
			{
				try
				{
					var order = await _sl.GetAsync<SlSalesOrder>(
						$"Orders({baseEntry.Value})?$select=U_SEI_PO_EDI", ct);
					if (!string.IsNullOrWhiteSpace(order?.U_SEI_PO_EDI))
						return order.U_SEI_PO_EDI.Trim();
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "DESADV: no se pudo leer U_SEI_PO_EDI del pedido origen {BaseEntry}", baseEntry);
				}
			}

			// 3. Fallback: mismo GLN del comprador
			_logger.LogWarning(
				"DESADV: albarán {DocNum} sin GLN punto entrega — usando GLN cliente como fallback.",
				dn.DocNum);
			return glnClienteFallback;
		}

		private async Task<DesadvAlbaran?> BuildAlbaranAsync(
			SlDeliveryNote dn, string glnCliente, CancellationToken ct)
		{
			if (dn.DocumentLines == null || dn.DocumentLines.Count == 0)
			{
				_logger.LogWarning("DESADV: albarán {DocNum} sin líneas, se omite.", dn.DocNum);
				return null;
			}

			var glnPuntoEntrega = await GetGlnPuntoEntregaAsync(dn, glnCliente, ct);
			var docNumPedido = await GetDocNumPedidoAsync(dn, ct);

			var albaran = new DesadvAlbaran
			{
				DocEntry = dn.DocEntry,
				DocNum = dn.DocNum.ToString(),
				NumAtCard = dn.NumAtCard?.Trim() ?? "",
				DocDate = FormatDateTime(dn.DocDate),
				DocDueDate = FormatDateTime(dn.DocDueDate),
				GlnCliente = glnCliente,
				GlnPuntoEntrega = glnPuntoEntrega ?? glnCliente,
				DocNumPedido = docNumPedido,
			};

			// Validación informativa: si el rango de números de bulto (min..max) no coincide
			// con la cantidad de bultos distintos que realmente tienen líneas, lo logueamos
			// pero NO fabricamos bultos vacíos en el fichero — solo se emiten los que tienen líneas.
			var bultosReales = dn.DocumentLines
				.SelectMany(l => ParsearBultosLinea(l, dn.DocNum))
				.Select(b => b.NumeroBulto)
				.ToList();

			if (bultosReales.Count > 0)
			{
				var min = bultosReales.Min();
				var max = bultosReales.Max();
				var distintos = bultosReales.Distinct().Count();
				var rangoEsperado = (int)(max - min) + 1;

				if (distintos != rangoEsperado)
				{
					_logger.LogWarning(
						"DESADV: albarán {DocNum} — rango de bultos {Min}-{Max} esperaría {Rango} bulto(s), pero solo {Distintos} tienen línea(s).",
						dn.DocNum, min, max, rangoEsperado, distintos);
				}
			}

			var packsHabilitados = _packsOptions.Value.EnviarPacksUnificados;

			// ── 1. Construir la línea de salida por cada línea SAP (o unificada por pack, si está habilitado) ──
			var lineasCrudas = new List<(DesadvLinea Linea, decimal? BultoReal)>();

			var cantidadPorGrupoPack = packsHabilitados
				? dn.DocumentLines
					.Where(l => !string.IsNullOrWhiteSpace(l.U_INTRX_KT_PACK))
					.GroupBy(l => (l.U_INTRX_KT_PACK, l.U_INTRX_KT_QPACK))
					.ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity))
				: new Dictionary<(string?, decimal?), decimal>();

			var gruposPackEmitidos = new HashSet<(string?, decimal?)>();

			// Tabla interna de bultos: se carga recién si alguna línea no trae bulto en sus campos.
			Dictionary<string, List<PackingSlot>>? packingPool = null;

			foreach (var ln in dn.DocumentLines)
			{
				ct.ThrowIfCancellationRequested();

				if (packsHabilitados && !string.IsNullOrWhiteSpace(ln.U_INTRX_KT_PACK))
				{
					var key = (ln.U_INTRX_KT_PACK, ln.U_INTRX_KT_QPACK);
					if (!gruposPackEmitidos.Add(key))
						continue; // ya se emitió la línea unificada de este pack

					var packCode = ln.U_INTRX_KT_PACK!;
					var pack = await _packResolver.GetPackByCodeAsync(packCode, ct);

					// Una línea de pack unificado se queda en un solo bulto (se toma el primero
					// que traiga la línea origen); repartir un pack entre varias cajas no aplica aquí.
					var bultosPack = ParsearBultosLinea(ln, dn.DocNum);
					decimal? bultoPack = bultosPack.Count > 0 ? bultosPack[0].NumeroBulto : null;

					lineasCrudas.Add((new DesadvLinea
					{
						ItemCode = packCode,
						Descripcion = NormalizeText(pack?.Descripcion ?? ""),
						Ean = pack?.Ean ?? "",
						Cantidad = cantidadPorGrupoPack[key],
					}, bultoPack));
				}
				else
				{
					var ean = ln.BarCode?.Trim();
					if (string.IsNullOrWhiteSpace(ean))
						ean = await GetEanFromItemAsync(ln.ItemCode ?? "", ct);

					var bultosLinea = ParsearBultosLinea(ln, dn.DocNum);
					var desdeTabla = false;

					// Último recurso: si los campos de la línea no traen bulto, se usa la tabla
					// interna de bultos (@SEI_LINEAS_PACKINGL), cargada una sola vez por albarán.
					if (bultosLinea.Count == 0)
					{
						packingPool ??= await CargarTablaPackingAsync(dn, ct);
						bultosLinea = AsignarDesdeTablaPacking(ln, packingPool);
						desdeTabla = bultosLinea.Count > 0;
					}

					if (bultosLinea.Count == 0)
					{
						// Sin info de bulto: una sola línea de salida; el bulto se asigna
						// más adelante (fallback: cada una en su propia caja).
						lineasCrudas.Add((new DesadvLinea
						{
							ItemCode = ln.ItemCode ?? "",
							Descripcion = NormalizeText(ln.ItemDescription ?? ""),
							Ean = ean ?? "",
							Cantidad = ln.Quantity,
						}, null));
					}
					else
					{
						var sumaBultos = bultosLinea.Sum(b => b.Cantidad);
						if (sumaBultos != ln.Quantity)
						{
							_logger.LogWarning(
								"DESADV: albarán {DocNum} línea {ItemCode} — la suma de cantidades por bulto ({Suma}, origen: {Origen}) no coincide con la cantidad de la línea ({Cantidad}).",
								dn.DocNum, ln.ItemCode, sumaBultos, desdeTabla ? "tabla interna" : "U_FC_QBultos", ln.Quantity);
						}

						foreach (var (numBulto, cantidad) in bultosLinea)
						{
							lineasCrudas.Add((new DesadvLinea
							{
								ItemCode = ln.ItemCode ?? "",
								Descripcion = NormalizeText(ln.ItemDescription ?? ""),
								Ean = ean ?? "",
								Cantidad = cantidad,
							}, numBulto));
						}

						// Si la tabla interna no cubre toda la línea, lo que falta no se pierde:
						// sale como línea aparte sin bulto asignado (su propia caja).
						if (desdeTabla && sumaBultos < ln.Quantity)
						{
							lineasCrudas.Add((new DesadvLinea
							{
								ItemCode = ln.ItemCode ?? "",
								Descripcion = NormalizeText(ln.ItemDescription ?? ""),
								Ean = ean ?? "",
								Cantidad = ln.Quantity - sumaBultos,
							}, null));
						}
					}
				}
			}

			// ── 2. Agrupar por bulto real (U_SEI_NumBulto) para que cada caja física
			//      quede contigua en el fichero, con un único SSCC por bulto. Las líneas
			//      sin bulto informado caen cada una en su propia caja (comportamiento previo). ──
			var ordenBultos = new List<decimal>();
			var lineasPorBulto = new Dictionary<decimal, List<DesadvLinea>>();
			decimal siguienteSinBulto = -1m;

			foreach (var (linea, bultoReal) in lineasCrudas)
			{
				var clave = bultoReal ?? siguienteSinBulto--;
				if (!lineasPorBulto.TryGetValue(clave, out var lista))
				{
					lista = new List<DesadvLinea>();
					lineasPorBulto[clave] = lista;
					ordenBultos.Add(clave);
				}
				lista.Add(linea);
			}

			// Las cajas "sin bulto" toman números correlativos por encima del mayor bulto real
			// del albarán, para que nunca repitan el SSCC de un bulto real (los de la tabla
			// interna son números chicos: 1, 2, 3...).
			var siguienteBultoLibre = (int)ordenBultos.Where(c => c > 0).DefaultIfEmpty(0).Max();

			int lineIdx = 0;
			foreach (var clave in ordenBultos)
			{
				// Bulto real (positivo) si vino de SAP o de la tabla interna; si es el sentinel
				// de "sin bulto" (negativo), se asigna el siguiente número libre.
				var bultoParaSscc = clave > 0 ? (int)clave : ++siguienteBultoLibre;

				foreach (var linea in lineasPorBulto[clave])
				{
					linea.LineNum = lineIdx;
					linea.Bulto = bultoParaSscc;
					albaran.Lineas.Add(linea);
					lineIdx++;
				}
			}

			_logger.LogInformation(
				"DESADV: albarán {DocNum} — {Count} línea(s) — GLN_BY={Gln} GLN_DP={Dp}",
				albaran.DocNum, albaran.Lineas.Count, glnCliente, albaran.GlnPuntoEntrega);

			return albaran;
		}

		/// <summary>
		/// Devuelve la lista de (número de bulto, cantidad) en que se reparte una línea.
		/// Prioridad: U_FC_Bultos/U_FC_QBultos (listas separadas por ";", ej. "15741;15742" /
		/// "1;2"). Si no vienen, cae a U_SEI_NumBulto (un solo bulto con toda la cantidad de
		/// la línea). Si tampoco hay, devuelve lista vacía (sin info de bulto).
		/// </summary>
		private List<(decimal NumeroBulto, decimal Cantidad)> ParsearBultosLinea(SlDeliveryLine ln, int docNum)
		{
			if (!string.IsNullOrWhiteSpace(ln.U_FC_Bultos) && !string.IsNullOrWhiteSpace(ln.U_FC_QBultos))
			{
				var bultos = ln.U_FC_Bultos.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				var cantidades = ln.U_FC_QBultos.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

				if (bultos.Length != cantidades.Length)
				{
					_logger.LogWarning(
						"DESADV: albarán {DocNum} línea {ItemCode} — U_FC_Bultos ({NBultos}) y U_FC_QBultos ({NCantidades}) tienen distinta cantidad de elementos, se ignoran.",
						docNum, ln.ItemCode, bultos.Length, cantidades.Length);
				}
				else if (bultos.Length > 0)
				{
					var resultado = new List<(decimal, decimal)>();
					var ok = true;

					for (int i = 0; i < bultos.Length; i++)
					{
						if (decimal.TryParse(bultos[i], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var numBulto) &&
							decimal.TryParse(cantidades[i], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cant))
						{
							resultado.Add((numBulto, cant));
						}
						else
						{
							_logger.LogWarning(
								"DESADV: albarán {DocNum} línea {ItemCode} — no se pudo parsear el elemento {Idx} de U_FC_Bultos/U_FC_QBultos ('{Bulto}'/'{Cantidad}').",
								docNum, ln.ItemCode, i, bultos[i], cantidades[i]);
							ok = false;
							break;
						}
					}

					if (ok) return resultado;
				}
			}

			if (ln.U_SEI_NumBulto.HasValue)
				return new List<(decimal, decimal)> { (ln.U_SEI_NumBulto.Value, ln.Quantity) };

			return new List<(decimal, decimal)>();
		}

		private sealed class PackingSlot
		{
			public decimal Bulto { get; init; }
			public decimal Restante { get; set; }
		}

		/// <summary>
		/// Carga la tabla interna de bultos (@SEI_LINEAS_PACKINGL) del albarán, agrupada por
		/// artículo. Busca primero las filas cuyo U_DocEntry es el DocNum del albarán (líneas
		/// completadas a mano); si no hay, usa las del documento origen (picking hecho sobre
		/// el pedido, BaseEntry de las líneas), para no contar dos veces el mismo bulto.
		/// </summary>
		private async Task<Dictionary<string, List<PackingSlot>>> CargarTablaPackingAsync(
			SlDeliveryNote dn, CancellationToken ct)
		{
			var pool = new Dictionary<string, List<PackingSlot>>(StringComparer.OrdinalIgnoreCase);

			try
			{
				var origen = "albarán";
				var filas = await GetPackingRowsAsync(new[] { dn.DocNum.ToString() }, ct);

				if (filas.Count == 0)
				{
					var baseEntries = dn.DocumentLines
						.Where(l => l.BaseEntry.HasValue && l.BaseEntry.Value > 0)
						.Select(l => l.BaseEntry!.Value.ToString())
						.Distinct()
						.ToArray();

					if (baseEntries.Length > 0)
					{
						filas = await GetPackingRowsAsync(baseEntries, ct);
						origen = "documento origen";
					}
				}

				foreach (var fila in filas.OrderBy(f => f.U_Linea).ThenBy(f => f.Code, StringComparer.Ordinal))
				{
					if (string.IsNullOrWhiteSpace(fila.U_ItemCode) || fila.U_Cantidad <= 0) continue;

					if (!pool.TryGetValue(fila.U_ItemCode.Trim(), out var slots))
					{
						slots = new List<PackingSlot>();
						pool[fila.U_ItemCode.Trim()] = slots;
					}
					slots.Add(new PackingSlot { Bulto = fila.U_NBulto, Restante = fila.U_Cantidad });
				}

				_logger.LogInformation(
					"DESADV: albarán {DocNum} — tabla interna de bultos: {Filas} fila(s) (origen: {Origen}).",
					dn.DocNum, filas.Count, origen);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex,
					"DESADV: albarán {DocNum} — no se pudo leer la tabla interna de bultos; se usa una caja por línea.",
					dn.DocNum);
			}

			return pool;
		}

		private async Task<List<SlPackingLine>> GetPackingRowsAsync(string[] docEntries, CancellationToken ct)
		{
			var condiciones = string.Join(" or ",
				docEntries.Select(d => $"U_DocEntry eq '{d.Replace("'", "''")}'"));
			var filter = Uri.EscapeDataString(condiciones);

			var resultado = new List<SlPackingLine>();
			string? url =
				$"U_SEI_LINEAS_PACKINGL?$filter={filter}&$orderby=Code" +
				"&$select=Code,U_DocEntry,U_Linea,U_ItemCode,U_NBulto,U_Cantidad";

			// Service Layer pagina de a 20 filas: se sigue odata.nextLink hasta agotarlo.
			while (!string.IsNullOrEmpty(url))
			{
				ct.ThrowIfCancellationRequested();
				var pagina = await _sl.GetAsync<SlPackingListResponse>(url, ct);
				if (pagina?.Value == null) break;
				resultado.AddRange(pagina.Value);
				url = pagina.NextLink;
			}

			return resultado;
		}

		/// <summary>
		/// Reparte la cantidad de la línea entre los bultos de la tabla interna que
		/// corresponden a su artículo, consumiendo las filas en orden (si el mismo artículo
		/// está en dos líneas del albarán, cada una toma lo que le toca).
		/// </summary>
		private static List<(decimal NumeroBulto, decimal Cantidad)> AsignarDesdeTablaPacking(
			SlDeliveryLine ln, Dictionary<string, List<PackingSlot>> pool)
		{
			var resultado = new List<(decimal, decimal)>();

			if (string.IsNullOrWhiteSpace(ln.ItemCode) || !pool.TryGetValue(ln.ItemCode.Trim(), out var slots))
				return resultado;

			var pendiente = ln.Quantity;
			foreach (var slot in slots)
			{
				if (pendiente <= 0) break;
				if (slot.Restante <= 0) continue;

				var tomar = Math.Min(slot.Restante, pendiente);
				resultado.Add((slot.Bulto, tomar));
				slot.Restante -= tomar;
				pendiente -= tomar;
			}

			return resultado;
		}

		private async Task<string?> GetEanFromItemAsync(string itemCode, CancellationToken ct)
		{
			if (string.IsNullOrWhiteSpace(itemCode)) return null;
			try
			{
				var escaped = itemCode.Replace("'", "''");
				var item = await _sl.GetAsync<SlItem>($"Items('{escaped}')?$select=BarCode", ct);
				return item?.BarCode?.Trim();
			}
			catch { return null; }
		}

		private static string FormatDateTime(string? sapDate)
		{
			if (string.IsNullOrWhiteSpace(sapDate)) return "";
			if (DateTime.TryParse(sapDate, out var dt)) return dt.ToString("yyyyMMddHHmm");
			return sapDate;
		}

		private static string NormalizeText(string text)
		{
			var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
			var sb = new System.Text.StringBuilder();
			foreach (var c in normalized)
			{
				var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
				if (cat != System.Globalization.UnicodeCategory.NonSpacingMark)
					sb.Append(c);
			}
			return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
		}


		private async Task<string> GetDocNumPedidoAsync(SlDeliveryNote dn, CancellationToken ct)
		{
			try
			{
				var baseEntry = dn.DocumentLines?.FirstOrDefault(l => l.BaseEntry.HasValue && l.BaseEntry.Value > 0)?.BaseEntry;
				if (!baseEntry.HasValue) return "";

				var order = await _sl.GetAsync<SlSalesOrder>(
					$"Orders({baseEntry.Value})?$select=DocNum", ct);

				return order?.DocNum.ToString() ?? "";
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "DESADV: no se pudo leer DocNum del pedido origen.");
				return "";
			}
		}


	}
}