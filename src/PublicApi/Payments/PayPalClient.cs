using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalClient
{
    private readonly IHttpClientFactory _factory; private readonly PayPalOptions _options; private string? _token; private DateTimeOffset _tokenExpires;
    public PayPalClient(IHttpClientFactory factory, IOptions<PayPalOptions> options) { _factory = factory; _options = options.Value; }
    private string BaseUrl => (_options.BaseUrl ?? (_options.Environment.Equals("live", StringComparison.OrdinalIgnoreCase) ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com")).TrimEnd('/');
    private async Task<HttpClient> ClientAsync(CancellationToken ct)
    {
        var c = _factory.CreateClient();
        if (_token is null || _tokenExpires <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1/oauth2/token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
            req.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string,string>("grant_type", "client_credentials") });
            using var response = await c.SendAsync(req, ct); await Ensure(response);
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct) ?? throw new InvalidOperationException("PayPal returned no access token.");
            _token = token.AccessToken; _tokenExpires = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
        }
        c.BaseAddress = new Uri(BaseUrl + "/"); c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token); return c;
    }
    public async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, string requestId, CancellationToken ct)
    {
        var c = await ClientAsync(ct); using var req = new HttpRequestMessage(method, path); req.Headers.Add("PayPal-Request-Id", requestId); req.Headers.Add("Prefer", "return=representation");
        if (body is not null) req.Content = JsonContent.Create(body); using var response = await c.SendAsync(req, ct); var text = await response.Content.ReadAsStringAsync(ct); if (!response.IsSuccessStatusCode) throw new PayPalException((int)response.StatusCode, text); return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }
    public async Task<JsonDocument> GetAsync(string path, CancellationToken ct) => await SendAsync(HttpMethod.Get, path, null, Guid.NewGuid().ToString("N"), ct);
    public async Task DeleteAsync(string path, CancellationToken ct) { var c = await ClientAsync(ct); using var req = new HttpRequestMessage(HttpMethod.Delete, path); using var response = await c.SendAsync(req, ct); if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound) throw new PayPalException((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct)); }
    private static async Task Ensure(HttpResponseMessage response) { if (!response.IsSuccessStatusCode) throw new InvalidOperationException("PayPal authentication failed."); }
    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken, [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
public sealed class PayPalException : Exception { public int StatusCode { get; } public string Payload { get; } public PayPalException(int statusCode, string payload) : base($"PayPal rejected the operation ({statusCode}).") { StatusCode = statusCode; Payload = payload; } }
