using EDI_Conector_FC.Models.SapModels;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RestSharp;
using System.Net;

namespace EDI_Conector_FC.Services.ServiceSAP
{
	public class ServiceLoginSL
	{
		private readonly SapOptions _opt;

		public ServiceLoginSL(IOptions<SapOptions> opt)
		{
			_opt = opt.Value;
		}

		public async Task<(string SessionId, string? RouteId)> LoginAsync()
		{
			var login = new
			{
				CompanyDB = _opt.Company,
				UserName = _opt.UserName,
				Password = _opt.Password
			};

			var handler = new HttpClientHandler();
			if (_opt.IgnoreSslErrors)
			{
				handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
			}

			var httpClient = new HttpClient(handler)
			{
				Timeout = TimeSpan.FromSeconds(_opt.TimeoutSeconds),
				BaseAddress = new Uri($"{_opt.Server}/b1s/v1/Login")
			};

			var client = new RestClient(httpClient);

			var objJson = JsonConvert.SerializeObject(login);

			var request = new RestRequest();
			request.AddHeader("Content-Type", "application/json");
			request.Method = Method.Post;
			request.AddParameter("application/json", objJson, ParameterType.RequestBody);

			var response = await client.ExecuteAsync(request);

			if (response.StatusCode != HttpStatusCode.OK)
				throw new Exception($"Login SL falló: {(int)response.StatusCode} {response.StatusDescription} - {response.Content}");

			// SessionId viene en JSON
			using var doc = System.Text.Json.JsonDocument.Parse(response.Content!);
			var sessionId = doc.RootElement.GetProperty("SessionId").GetString() ?? "";

			// ROUTEID a veces viene en Set-Cookie
			string? routeId = null;
			if (response.Headers != null)
			{
				var setCookies = response.Headers
					.Where(h => string.Equals(h.Name?.ToString(), "Set-Cookie", StringComparison.OrdinalIgnoreCase))
					.Select(h => h.Value?.ToString())
					.Where(v => !string.IsNullOrWhiteSpace(v))
					.ToList();

				foreach (var sc in setCookies)
				{
					var idx = sc!.IndexOf("ROUTEID=", StringComparison.OrdinalIgnoreCase);
					if (idx >= 0)
					{
						var sub = sc.Substring(idx + "ROUTEID=".Length);
						routeId = sub.Split(';')[0];
						break;
					}
				}
			}

			return (sessionId, routeId);
		}
	}
}
