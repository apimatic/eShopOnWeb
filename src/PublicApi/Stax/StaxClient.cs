using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>
/// Thin wrapper over the Stax REST API, called directly over HTTP.
/// </summary>
public class StaxClient
{
    private readonly HttpClient _http;
    private readonly StaxSettings _settings;

    public StaxClient(HttpClient http, IOptions<StaxSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    public bool HasPlatformKey => !string.IsNullOrWhiteSpace(_settings.PlatformApiKey);

    private string BaseUrl =>
        (string.IsNullOrWhiteSpace(_settings.BaseUrl) ? StaxSettings.DefaultBaseUrl : _settings.BaseUrl!).TrimEnd('/');

    public async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body = null,
        bool platform = false, CancellationToken ct = default)
    {
        var key = platform ? _settings.PlatformApiKey : _settings.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw platform
                ? ApiException.Unprocessable("Seller onboarding is not available: the shop has no Stax platform key configured.")
                : new ApiException(503, "Payments are not configured: Stax:ApiKey is missing.");
        }

        using var req = new HttpRequestMessage(method, BaseUrl + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null)
        {
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(502, "Could not reach Stax: " + ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(504, "Stax did not answer in time.");
        }

        using (resp)
        {
            var text = await resp.Content.ReadAsStringAsync(ct);
            JsonNode? json = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { json = JsonNode.Parse(text); } catch (JsonException) { }
            }

            if (resp.IsSuccessStatusCode) return json;
            throw MapError(resp.StatusCode, json, text);
        }
    }

    private static ApiException MapError(HttpStatusCode status, JsonNode? json, string raw)
    {
        var reason = DescribeError(json) ?? (string.IsNullOrWhiteSpace(raw) ? status.ToString() : Truncate(raw, 300));
        return (int)status switch
        {
            404 => new ApiException(404, reason),
            401 or 403 => new ApiException(502, "Stax rejected the shop's credentials."),
            429 => new ApiException(503, "Stax is rate limiting requests, try again shortly."),
            >= 500 => new ApiException(502, "Stax failed to process the request: " + reason),
            _ => new ApiException(422, reason),
        };
    }

    /// <summary>Flattens Stax validation errors ({"field":["msg"]}) into one sentence.</summary>
    public static string? DescribeError(JsonNode? json)
    {
        if (json is JsonObject obj)
        {
            var parts = new List<string>();
            foreach (var (name, value) in obj)
            {
                if (value is JsonArray arr)
                {
                    foreach (var item in arr) if (item != null) parts.Add(item.ToString());
                }
                else if (value is JsonValue v)
                {
                    parts.Add(name == "message" || name == "error" ? v.ToString() : $"{name}: {v}");
                }
            }
            if (parts.Count > 0) return string.Join(" ", parts.Distinct());
        }
        return null;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];

    public static string Money(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);

    // ---- customers ----

    public async Task<string> CreateCustomerAsync(object body, CancellationToken ct = default)
    {
        var r = await SendAsync(HttpMethod.Post, "/customer", body, ct: ct);
        return r?["id"]?.GetValue<string>() ?? throw new ApiException(502, "Stax did not return a customer id.");
    }

    public Task<JsonNode?> UpdateCustomerAsync(string id, object body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Put, $"/customer/{Uri.EscapeDataString(id)}", body, ct: ct);

    public Task<JsonNode?> DeleteCustomerAsync(string id, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"/customer/{Uri.EscapeDataString(id)}", ct: ct);

    /// <summary>Finds Stax customers whose email is exactly the given one (Stax matches by substring).</summary>
    public async Task<List<string>> FindCustomerIdsByEmailAsync(string email, CancellationToken ct = default)
    {
        var ids = new List<string>();
        for (var page = 1; page <= 50; page++)
        {
            var r = await SendAsync(HttpMethod.Get,
                $"/customer?email={Uri.EscapeDataString(email)}&per_page=100&page={page}", ct: ct);
            foreach (var c in r?["data"]?.AsArray() ?? new JsonArray())
            {
                var e = c?["email"]?.GetValue<string>();
                if (string.Equals(e, email, StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(c!["id"]!.GetValue<string>());
                }
            }
            if (page >= (r?["last_page"]?.GetValue<int>() ?? 1)) break;
        }
        return ids;
    }

    // ---- payment methods ----

    public async Task<JsonNode> CreatePaymentMethodAsync(object body, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Post, "/payment-method", body, ct: ct)
           ?? throw new ApiException(502, "Stax returned an empty payment method.");

    public Task<JsonNode?> GetPaymentMethodAsync(string id, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"/payment-method/{Uri.EscapeDataString(id)}", ct: ct);

    public Task<JsonNode?> UpdatePaymentMethodAsync(string id, object body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Put, $"/payment-method/{Uri.EscapeDataString(id)}", body, ct: ct);

    public Task<JsonNode?> DeletePaymentMethodAsync(string id, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"/payment-method/{Uri.EscapeDataString(id)}", ct: ct);

    /// <summary>All payment methods of a customer at Stax, removed ones included.</summary>
    public async Task<List<JsonNode>> ListPaymentMethodsAsync(string customerId, CancellationToken ct = default)
    {
        var all = new List<JsonNode>();
        for (var page = 1; page <= 100; page++)
        {
            var r = await SendAsync(HttpMethod.Get,
                $"/payment-method?customer_id={Uri.EscapeDataString(customerId)}&status=all&per_page=100&page={page}", ct: ct);
            foreach (var pm in r?["data"]?.AsArray() ?? new JsonArray())
            {
                if (pm != null && pm["customer_id"]?.GetValue<string>() == customerId) all.Add(pm);
            }
            if (page >= (r?["last_page"]?.GetValue<int>() ?? 1)) break;
        }
        return all;
    }

    // ---- payments ----

    public async Task<JsonNode> ReviewSurchargeAsync(string paymentMethodId, decimal subtotal, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Get,
               $"/surcharge/review?payment_method_id={Uri.EscapeDataString(paymentMethodId)}&total={Money(subtotal)}", ct: ct)
           ?? throw new ApiException(502, "Stax returned no surcharge information.");

    public async Task<JsonNode> ChargeAsync(string paymentMethodId, decimal total, object meta, string idempotencyId,
        CancellationToken ct = default)
        => await SendAsync(HttpMethod.Post, "/charge", new
        {
            total,
            payment_method_id = paymentMethodId,
            pre_auth = false,
            meta,
            idempotency_id = idempotencyId,
            channel = "eshoponweb-public-api",
        }, ct: ct) ?? throw new ApiException(502, "Stax returned an empty transaction.");

    public async Task<JsonNode> GetTransactionAsync(string id, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Get, $"/transaction/{Uri.EscapeDataString(id)}", ct: ct)
           ?? throw new ApiException(502, "Stax returned an empty transaction.");

    public async Task<JsonNode> VoidOrRefundAsync(string id, decimal total, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Post, $"/transaction/{Uri.EscapeDataString(id)}/void-or-refund?total={Money(total)}", new { total }, ct: ct)
           ?? throw new ApiException(502, "Stax returned an empty transaction.");

    // ---- invoice schedules ----

    public async Task<JsonNode> CreateScheduleAsync(object body, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Post, "/invoice/schedule", body, ct: ct)
           ?? throw new ApiException(502, "Stax returned an empty schedule.");

    public async Task<JsonNode> GetScheduleAsync(string id, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Get, $"/invoice/schedule/{Uri.EscapeDataString(id)}", ct: ct)
           ?? throw new ApiException(502, "Stax returned an empty schedule.");

    public async Task<JsonNode> UpdateScheduleAsync(string id, object body, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Put, $"/invoice/schedule/{Uri.EscapeDataString(id)}", body, ct: ct)
           ?? throw new ApiException(502, "Stax returned an empty schedule.");

    // ---- partner ----

    public async Task<JsonNode> CreateMerchantAsync(object body, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Post, "/merchant", body, platform: true, ct: ct)
           ?? throw new ApiException(502, "Stax returned an empty merchant.");
}
