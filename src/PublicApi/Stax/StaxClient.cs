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

/// <summary>Plain HTTP client for the Stax API (no SDK).</summary>
public class StaxClient
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<StaxSettings> _settings;

    public StaxClient(HttpClient http, IOptionsMonitor<StaxSettings> settings)
    {
        _http = http;
        _settings = settings;
    }

    public bool HasPlatformKey => !string.IsNullOrWhiteSpace(_settings.CurrentValue.PlatformApiKey);

    private string Url(string path)
    {
        var baseUrl = _settings.CurrentValue.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = StaxSettings.DEFAULT_BASE_URL;
        return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    public async Task<JsonNode> SendAsync(HttpMethod method, string path, object? body = null, bool platform = false, CancellationToken ct = default)
    {
        var key = platform ? _settings.CurrentValue.PlatformApiKey : _settings.CurrentValue.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new StaxException(503, platform ? "The Stax platform key is not configured." : "The Stax API key is not configured.");

        using var request = new HttpRequestMessage(method, Url(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !ct.IsCancellationRequested)
        {
            throw new StaxException(0, "Stax could not be reached.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonNode? json = null;
            try { json = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); } catch (JsonException) { }

            if (!response.IsSuccessStatusCode)
                throw new StaxException((int)response.StatusCode, ExtractError(json, response.StatusCode));
            return json ?? new JsonObject();
        }
    }

    private static string ExtractError(JsonNode? json, HttpStatusCode status)
    {
        var parts = new List<string>();
        if (json is JsonObject top && top["message"] is JsonValue mv && mv.TryGetValue<string>(out var topMessage) && !string.IsNullOrWhiteSpace(topMessage))
            return topMessage;
        if (json is JsonObject obj)
        {
            foreach (var (name, value) in obj)
            {
                if (value is JsonValue v && v.TryGetValue<string>(out var s)) parts.Add(name == "message" ? s : $"{name}: {s}");
                else if (value is JsonArray arr) parts.AddRange(arr.Select(a => $"{name}: {a}"));
                else if (value is JsonObject inner && name == "error") parts.Add(inner["message"]?.ToString() ?? inner.ToJsonString());
            }
        }
        var msg = string.Join("; ", parts.Distinct());
        return string.IsNullOrWhiteSpace(msg) ? $"Stax answered {(int)status}." : msg;
    }

    // ---- customers

    public async Task<List<JsonNode>> FindCustomersByEmailAsync(string email, CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get, $"customer?per_page=100&email={Uri.EscapeDataString(email)}", ct: ct);
        return Rows(json)
            .Where(c => string.Equals(c["email"]?.GetValue<string>(), email, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public Task<JsonNode> CreateCustomerAsync(string email, string firstName, string lastName, string? company, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "customer", new { firstname = firstName, lastname = lastName, email, company = company ?? "" }, ct: ct);

    public Task<JsonNode> UpdateCustomerAsync(string id, object body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Put, $"customer/{Uri.EscapeDataString(id)}", body, ct: ct);

    public Task<JsonNode> DeleteCustomerAsync(string id, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"customer/{Uri.EscapeDataString(id)}", ct: ct);

    // ---- payment methods

    public Task<JsonNode> CreatePaymentMethodAsync(object body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "payment-method", body, ct: ct);

    public Task<JsonNode> GetPaymentMethodAsync(string id, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"payment-method/{Uri.EscapeDataString(id)}", ct: ct);

    public Task<JsonNode> DeletePaymentMethodAsync(string id, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"payment-method/{Uri.EscapeDataString(id)}", ct: ct);

    public Task<JsonNode> UpdatePaymentMethodAsync(string id, object body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Put, $"payment-method/{Uri.EscapeDataString(id)}", body, ct: ct);

    /// <summary>The customer's payment methods that have not been removed.</summary>
    public async Task<List<JsonNode>> ListActivePaymentMethodsAsync(string customerId, CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get, $"customer/{Uri.EscapeDataString(customerId)}/payment-method", ct: ct);
        return Rows(json).Where(p => p["deleted_at"] is null).ToList();
    }

    /// <summary>Every payment method the customer ever saved, including removed ones.</summary>
    public async Task<List<JsonNode>> ListAllPaymentMethodsAsync(string customerId, CancellationToken ct = default)
    {
        var all = new List<JsonNode>();
        for (var page = 1; page <= 500; page++)
        {
            var json = await SendAsync(HttpMethod.Get,
                $"payment-method?status=all&per_page=200&page={page}&customer_id={Uri.EscapeDataString(customerId)}", ct: ct);
            all.AddRange(Rows(json));
            var last = json["last_page"]?.GetValue<int>() ?? page;
            if (page >= last) break;
        }
        // The customer filter is checked again here: never show another shopper's methods.
        return all.Where(p => p["customer_id"]?.GetValue<string>() == customerId).ToList();
    }

    // ---- money

    public async Task<(decimal Fee, decimal Total)> ReviewSurchargeAsync(string paymentMethodId, decimal amount, CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"surcharge/review?payment_method_id={Uri.EscapeDataString(paymentMethodId)}&total={amount.ToString(CultureInfo.InvariantCulture)}", ct: ct);
        var fee = json["surcharge_amount"]?.GetValue<decimal>() ?? 0m;
        return (Math.Round(fee, 2), Math.Round(amount + fee, 2));
    }

    public Task<JsonNode> ChargeAsync(string paymentMethodId, decimal total, string idempotencyId, object meta, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "charge", new
        {
            payment_method_id = paymentMethodId,
            total,
            pre_auth = false,
            idempotency_id = idempotencyId,
            meta
        }, ct: ct);

    public Task<JsonNode> GetTransactionAsync(string id, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"transaction/{Uri.EscapeDataString(id)}", ct: ct);

    public Task<JsonNode> VoidOrRefundAsync(string transactionId, decimal total, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"transaction/{Uri.EscapeDataString(transactionId)}/void-or-refund", new { total }, ct: ct);

    // ---- invoice schedules

    public Task<JsonNode> CreateInvoiceScheduleAsync(string customerId, decimal total, string rule, string url, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "invoice/schedule/", new
        {
            customer_id = customerId,
            total,
            url,
            rule,
            meta = new { subtotal = total }
        }, ct: ct);

    public Task<JsonNode> GetInvoiceScheduleAsync(string id, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"invoice/schedule/{Uri.EscapeDataString(id)}", ct: ct);

    public Task<JsonNode> UpdateInvoiceScheduleAsync(string id, object body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Put, $"invoice/schedule/{Uri.EscapeDataString(id)}", body, ct: ct);

    // ---- merchants (platform key)

    public Task<JsonNode> CreateMerchantAsync(object body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "merchant", body, platform: true, ct: ct);

    private static IEnumerable<JsonNode> Rows(JsonNode json)
    {
        var arr = json as JsonArray ?? json["data"] as JsonArray;
        return arr is null ? Enumerable.Empty<JsonNode>() : arr.Where(n => n is not null).Select(n => n!);
    }

    public static DateTimeOffset? ParseStaxTime(JsonNode? node)
    {
        var s = node is JsonValue v && v.TryGetValue<string>(out var str) ? str : null;
        if (string.IsNullOrEmpty(s)) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
    }
}
