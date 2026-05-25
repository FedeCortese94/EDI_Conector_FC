using EDI_Conector_FC.Models.SapModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RestSharp;
using System.Text.Json;

namespace EDI_Conector_FC.Services.ServiceSAP.Http
{
	public class ServiceLayerClient
	{
		private readonly SapOptions _opt;
		private readonly ServiceLoginSL _login;
		private readonly ILogger<ServiceLayerClient> _logger;

		private string? _sessionId;
		private string? _routeId;

		public ServiceLayerClient(IOptions<SapOptions> opt, ServiceLoginSL login, ILogger<ServiceLayerClient> logger)
		{
			_opt = opt.Value;
			_login = login;
			_logger = logger;
		}

		private async Task EnsureSessionAsync()
		{
			if (!string.IsNullOrWhiteSpace(_sessionId))
				return;

			var (sid, rid) = await _login.LoginAsync();
			_sessionId = sid;
			_routeId = rid;

			_logger.LogInformation("ServiceLayer session creada. SessionId={Sid} RouteId={Rid}",
				_sessionId, _routeId ?? "");
		}

		private RestClient BuildClient(string baseUrl)
		{
			var handler = new HttpClientHandler();

			if (_opt.IgnoreSslErrors)
				handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

			var httpClient = new HttpClient(handler)
			{
				Timeout = TimeSpan.FromSeconds(_opt.TimeoutSeconds),
				BaseAddress = new Uri(baseUrl)
			};

			return new RestClient(httpClient);
		}

		public async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken ct = default)
		{
			var raw = await GetRawAsync(relativeUrl);

			if (string.IsNullOrWhiteSpace(raw))
				return default;

			return JsonSerializer.Deserialize<T>(raw, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			});
		}


		public async Task<string> GetRawAsync(string relativeUrl)
		{
			await EnsureSessionAsync();

			var client = BuildClient($"{_opt.Server}/b1s/v1/");

			var req = new RestRequest(relativeUrl, Method.Get);
			req.AddHeader("Accept", "application/json");

			var cookie = string.IsNullOrWhiteSpace(_routeId)
				? $"B1SESSION={_sessionId}"
				: $"B1SESSION={_sessionId}; ROUTEID={_routeId}";

			req.AddHeader("Cookie", cookie);

			var resp = await client.ExecuteAsync(req);

			// Si la sesión caduca, reloguea y reintenta 1 vez
			if ((int)resp.StatusCode == 401)
			{
				_logger.LogWarning("Sesión SL caducada (401). Re-login...");
				_sessionId = null;
				_routeId = null;

				await EnsureSessionAsync();

				cookie = string.IsNullOrWhiteSpace(_routeId)
					? $"B1SESSION={_sessionId}"
					: $"B1SESSION={_sessionId}; ROUTEID={_routeId}";

				req = new RestRequest(relativeUrl, Method.Get);
				req.AddHeader("Accept", "application/json");
				req.AddHeader("Cookie", cookie);

				resp = await client.ExecuteAsync(req);
			}

			if (!resp.IsSuccessful)
				throw new Exception($"GET SL falló: {(int)resp.StatusCode} {resp.StatusDescription} - {resp.Content}");

			return resp.Content ?? "";
		}

		public async Task<TResponse?> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest body)
		{
			await EnsureSessionAsync();

			var client = BuildClient($"{_opt.Server}/b1s/v1/");

			var req = new RestRequest(relativeUrl, Method.Post);
			req.AddHeader("Accept", "application/json");
			req.AddHeader("Content-Type", "application/json");

			var cookie = string.IsNullOrWhiteSpace(_routeId)
				? $"B1SESSION={_sessionId}"
				: $"B1SESSION={_sessionId}; ROUTEID={_routeId}";

			req.AddHeader("Cookie", cookie);

			var json = JsonSerializer.Serialize(body);
			req.AddStringBody(json, DataFormat.Json);

			var resp = await client.ExecuteAsync(req);

			// re-login si 401
			if ((int)resp.StatusCode == 401)
			{
				_logger.LogWarning("Sesión SL caducada (401) en POST. Re-login...");
				_sessionId = null;
				_routeId = null;

				await EnsureSessionAsync();

				cookie = string.IsNullOrWhiteSpace(_routeId)
					? $"B1SESSION={_sessionId}"
					: $"B1SESSION={_sessionId}; ROUTEID={_routeId}";

				req = new RestRequest(relativeUrl, Method.Post);
				req.AddHeader("Accept", "application/json");
				req.AddHeader("Content-Type", "application/json");
				req.AddHeader("Cookie", cookie);
				req.AddStringBody(json, DataFormat.Json);

				resp = await client.ExecuteAsync(req);
			}

			if (!resp.IsSuccessful)
				throw new Exception($"POST SL falló: {(int)resp.StatusCode} {resp.StatusDescription} - {resp.Content}");

			if (string.IsNullOrWhiteSpace(resp.Content))
				return default;

			return JsonSerializer.Deserialize<TResponse>(resp.Content, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			});
		}



	}
}
