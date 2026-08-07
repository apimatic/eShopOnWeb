using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal REST client covering the capabilities this integration needs: direct card
/// payments, vaulted-card payments, full refunds, and saving/removing vaulted cards.
/// Verified against PayPal's Orders v2, Payments v2 and Vault Payment Tokens v3 APIs.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly PayPalAccessTokenCache _tokenCache;
    private readonly IAppLogger<PayPalClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalClient(
        HttpClient httpClient,
        PayPalSettings settings,
        PayPalAccessTokenCache tokenCache,
        IAppLogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _tokenCache = tokenCache;
        _logger = logger;
    }

    public async Task<PayPalPaymentResult> CreateCardOrderAsync(
        decimal amount, string currencyCode, CardPaymentDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            intent = "CAPTURE",
            purchase_units = new[] { new { amount = Amount(amount, currencyCode) } },
            payment_source = new { card = CardBody(card) }
        };
        return await CreateAndReadCaptureAsync(body, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalPaymentResult> CreateVaultedCardOrderAsync(
        decimal amount, string currencyCode, string vaultId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            intent = "CAPTURE",
            purchase_units = new[] { new { amount = Amount(amount, currencyCode) } },
            payment_source = new { card = new { vault_id = vaultId } }
        };
        return await CreateAndReadCaptureAsync(body, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund");
        request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        // Empty body issues a full refund of the captured amount.
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var doc = await SendAsync(request, "refund payment", cancellationToken);
        var root = doc.RootElement;
        var refundId = root.GetProperty("id").GetString() ?? string.Empty;
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty;

        if (!IsRefundComplete(status))
            throw new PayPalException($"Refund did not complete (status: {status}).", (int)HttpStatusCode.BadGateway);

        return new PayPalRefundResult(refundId, status);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentDetails card, string? customerId, CancellationToken cancellationToken = default)
    {
        object body = customerId is null
            ? new { payment_source = new { card = CardBody(card) } }
            : new { customer = new { id = customerId }, payment_source = new { card = CardBody(card) } };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v3/vault/payment-tokens")
        {
            Content = JsonContent(body)
        };
        request.Headers.TryAddWithoutValidation("PayPal-Request-Id", Guid.NewGuid().ToString());

        using var doc = await SendAsync(request, "vault card", cancellationToken);
        var root = doc.RootElement;

        var vaultId = root.GetProperty("id").GetString()
            ?? throw new PayPalException("PayPal did not return a vault id for the saved card.", (int)HttpStatusCode.BadGateway);
        var resolvedCustomerId = root.TryGetProperty("customer", out var cust) && cust.TryGetProperty("id", out var cid)
            ? cid.GetString() ?? string.Empty
            : customerId ?? string.Empty;

        var safeCard = root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl)
            ? cardEl
            : default;

        var brand = GetString(safeCard, "brand");
        var last4 = GetString(safeCard, "last_digits");
        var expiry = GetString(safeCard, "expiry");
        var name = GetString(safeCard, "name");
        if (string.IsNullOrEmpty(name)) name = card.CardholderName;

        return new PayPalVaultedCard(vaultId, resolvedCustomerId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}");
        await AuthorizeAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        // 204 = removed; 404 = already gone, which is fine for a delete.
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            return;

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        throw ToPayPalException("delete vaulted card", response.StatusCode, content);
    }

    // --- helpers ---

    private async Task<PayPalPaymentResult> CreateAndReadCaptureAsync(
        object body, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v2/checkout/orders")
        {
            Content = JsonContent(body)
        };
        // PayPal-Request-Id is mandatory for single-step card orders and provides idempotency.
        request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);

        using var doc = await SendAsync(request, "create card order", cancellationToken);
        var root = doc.RootElement;

        var payPalOrderId = root.GetProperty("id").GetString() ?? string.Empty;
        var orderStatus = root.TryGetProperty("status", out var st) ? st.GetString() ?? string.Empty : string.Empty;

        if (!string.Equals(orderStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            throw new PayPalException($"Payment was not completed (status: {orderStatus}).", (int)HttpStatusCode.BadRequest);

        var captureId = ExtractCaptureId(root)
            ?? throw new PayPalException("PayPal reported the order complete but returned no capture id.", (int)HttpStatusCode.BadGateway);

        return new PayPalPaymentResult(payPalOrderId, captureId, orderStatus);
    }

    private static string? ExtractCaptureId(JsonElement root)
    {
        if (root.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.TryGetProperty("payments", out var payments)
                    && payments.TryGetProperty("captures", out var captures)
                    && captures.ValueKind == JsonValueKind.Array)
                {
                    foreach (var capture in captures.EnumerateArray())
                    {
                        if (capture.TryGetProperty("id", out var id))
                            return id.GetString();
                    }
                }
            }
        }
        return null;
    }

    private object Amount(decimal amount, string currencyCode) => new
    {
        currency_code = currencyCode,
        value = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
    };

    private object CardBody(CardPaymentDetails card)
    {
        var billing = new Dictionary<string, string> { ["country_code"] = string.IsNullOrWhiteSpace(card.CountryCode) ? "US" : card.CountryCode };
        if (!string.IsNullOrWhiteSpace(card.AddressLine1)) billing["address_line_1"] = card.AddressLine1;
        if (!string.IsNullOrWhiteSpace(card.AddressLine2)) billing["address_line_2"] = card.AddressLine2!;
        if (!string.IsNullOrWhiteSpace(card.City)) billing["admin_area_2"] = card.City;
        if (!string.IsNullOrWhiteSpace(card.State)) billing["admin_area_1"] = card.State;
        if (!string.IsNullOrWhiteSpace(card.PostalCode)) billing["postal_code"] = card.PostalCode;

        return new
        {
            number = card.Number,
            expiry = card.Expiry,
            security_code = card.SecurityCode,
            name = card.CardholderName,
            billing_address = billing
        };
    }

    private static string GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v)
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static bool IsRefundComplete(string status)
        => string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase);

    private static StringContent JsonContent(object body)
        => new(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

    /// <summary>Sends an authorized request and returns the parsed JSON, or throws.</summary>
    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, string action, CancellationToken cancellationToken)
    {
        await AuthorizeAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw ToPayPalException(action, response.StatusCode, content);

        return string.IsNullOrWhiteSpace(content) ? JsonDocument.Parse("{}") : JsonDocument.Parse(content);
    }

    private PayPalException ToPayPalException(string action, HttpStatusCode status, string content)
    {
        string? debugId = null;
        var detail = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("debug_id", out var dbg)) debugId = dbg.GetString();
            if (root.TryGetProperty("message", out var msg)) detail = msg.GetString() ?? string.Empty;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
            {
                var first = details[0];
                var issue = first.TryGetProperty("issue", out var iss) ? iss.GetString() : null;
                var desc = first.TryGetProperty("description", out var d) ? d.GetString() : null;
                if (!string.IsNullOrEmpty(issue) || !string.IsNullOrEmpty(desc))
                    detail = $"{detail} ({issue}: {desc})".Trim();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall back to a generic message (never echo raw content).
        }

        // Client-caused failures (declines, validation) map to 4xx; everything else is an upstream error.
        var mapped = (int)status is >= 400 and < 500 ? (int)HttpStatusCode.BadRequest : (int)HttpStatusCode.BadGateway;
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"PayPal failed to {action} (HTTP {(int)status})."
            : $"PayPal failed to {action}: {detail}";

        _logger.LogWarning($"PayPal error during '{action}': HTTP {(int)status}, debugId={debugId ?? "n/a"}.");
        return new PayPalException(message, mapped, debugId);
    }

    /// <summary>Adds a bearer token, fetching/refreshing it as needed.</summary>
    private async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_tokenCache.TryGet(out var cached))
            return cached;

        await _tokenCache.Gate.WaitAsync(cancellationToken);
        try
        {
            if (_tokenCache.TryGet(out cached))
                return cached;

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
                throw new PayPalException("PayPal credentials are not configured (PayPal:ClientId / PayPal:ClientSecret).", (int)HttpStatusCode.InternalServerError);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw ToPayPalException("authenticate", response.StatusCode, content);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var token = root.GetProperty("access_token").GetString()
                ?? throw new PayPalException("PayPal did not return an access token.", (int)HttpStatusCode.BadGateway);
            var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 300;

            _tokenCache.Set(token, TimeSpan.FromSeconds(expiresIn));
            return token;
        }
        finally
        {
            _tokenCache.Gate.Release();
        }
    }
}
