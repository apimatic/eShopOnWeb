using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST implementation of <see cref="IPaymentGateway"/> using server-side Advanced Card Payments.
/// Orders are created and captured in a single call; cards are vaulted via the two-step
/// setup-token -> payment-token flow. Access tokens are cached until shortly before expiry.
/// Full card details are only ever placed in the outbound request body — never persisted or logged.
/// </summary>
public class PayPalPaymentService : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentService> _logger;
    private readonly string _baseUrl;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalPaymentService(HttpClient http, IOptions<PayPalSettings> settings, ILogger<PayPalPaymentService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
        _baseUrl = _settings.ResolveBaseUrl();
    }

    public async Task<PaymentResult> ChargeCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalOrderRequest
        {
            PaymentSource = new PayPalPaymentSource { Card = ToWireCard(card) },
            PurchaseUnits = { new PayPalPurchaseUnit { Amount = ToAmount(amount, currency) } }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", request, idempotencyKey, cancellationToken);
        return ReadCaptureResult(doc.RootElement);
    }

    public async Task<PaymentResult> ChargeVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new PayPalOrderRequest
        {
            PaymentSource = new PayPalPaymentSource { Card = new PayPalCard { VaultId = vaultId } },
            PurchaseUnits = { new PayPalPurchaseUnit { Amount = ToAmount(amount, currency) } }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", request, idempotencyKey, cancellationToken);
        return ReadCaptureResult(doc.RootElement);
    }

    public async Task<VaultedCard> SaveCardAsync(CardDetails card, string? existingCustomerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Step 1: create a setup token holding the raw card.
        var setupRequest = new PayPalVaultRequest
        {
            PaymentSource = new PayPalPaymentSource { Card = ToWireCard(card) },
            Customer = existingCustomerId is null ? null : new PayPalCustomer { Id = existingCustomerId }
        };

        string setupTokenId;
        using (var setupDoc = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupRequest, idempotencyKey + "-setup", cancellationToken))
        {
            setupTokenId = GetString(setupDoc.RootElement, "id")
                ?? throw new PaymentGatewayException("PayPal did not return a setup token id.");
        }

        // Step 2: exchange the setup token for a permanent payment token.
        var tokenRequest = new PayPalVaultRequest
        {
            PaymentSource = new PayPalPaymentSource { Token = new PayPalTokenRef { Id = setupTokenId, Type = "SETUP_TOKEN" } }
        };

        using var tokenDoc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenRequest, idempotencyKey + "-token", cancellationToken);
        return ReadVaultedCard(tokenDoc.RootElement, card);
    }

    public async Task RemoveVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken);
    }

    public async Task<RefundResult> RefundAsync(string captureId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // An empty body means a full refund.
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            new { }, idempotencyKey, cancellationToken);
        var status = GetString(doc.RootElement, "status") ?? "UNKNOWN";
        var refundId = GetString(doc.RootElement, "id")
            ?? throw new PaymentGatewayException("PayPal did not return a refund id.");
        return new RefundResult(refundId, status);
    }

    // ---- HTTP plumbing -------------------------------------------------------------------------

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(idempotencyKey))
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildGatewayException(method, path, response.StatusCode, payload);

        // 204 No Content (e.g. delete) has no JSON body.
        if (string.IsNullOrWhiteSpace(payload))
            return JsonDocument.Parse("{}");

        return JsonDocument.Parse(payload);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
                return _accessToken;

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
                throw new PaymentGatewayException("PayPal credentials are not configured (PayPal:ClientId / PayPal:ClientSecret).");

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _http.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with status {StatusCode}.", (int)response.StatusCode);
                throw new PaymentGatewayException("Failed to obtain a PayPal access token.");
            }

            using var doc = JsonDocument.Parse(payload);
            var accessToken = GetString(doc.RootElement, "access_token")
                ?? throw new PaymentGatewayException("PayPal token response did not contain an access token.");
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds)
                ? seconds : 300;

            _accessToken = accessToken;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private PaymentGatewayException BuildGatewayException(HttpMethod method, string path, System.Net.HttpStatusCode status, string payload)
    {
        // Parse the PayPal error name + issue codes only. Never surface or log raw field values (may echo card data).
        string name = "UNKNOWN";
        var issues = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            name = GetString(doc.RootElement, "name") ?? GetString(doc.RootElement, "error") ?? name;
            if (doc.RootElement.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var issue = GetString(detail, "issue");
                    if (issue is not null) issues.Add(issue);
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body: keep the generic name, do not log the body (could contain sensitive data).
        }

        var issueText = issues.Count > 0 ? $" ({string.Join(", ", issues)})" : string.Empty;
        _logger.LogError("PayPal {Method} {Path} failed: {StatusCode} {Name}{Issues}.",
            method.Method, path, (int)status, name, issueText);
        return new PaymentGatewayException($"Payment gateway rejected the request: {name}{issueText}.");
    }

    // ---- Mapping helpers -----------------------------------------------------------------------

    private static PayPalCard ToWireCard(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = $"{card.ExpiryYear:D4}-{card.ExpiryMonth:D2}",
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = new PayPalAddress
        {
            Line1 = card.BillingAddress.Line1,
            City = card.BillingAddress.City,
            State = card.BillingAddress.State,
            PostalCode = card.BillingAddress.PostalCode,
            CountryCode = card.BillingAddress.CountryCode
        }
    };

    private static PayPalAmount ToAmount(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PaymentResult ReadCaptureResult(JsonElement root)
    {
        var orderId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal did not return an order id.");
        var orderStatus = GetString(root, "status") ?? "UNKNOWN";

        if (root.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array
            && units.GetArrayLength() > 0
            && units[0].TryGetProperty("payments", out var payments)
            && payments.TryGetProperty("captures", out var captures) && captures.ValueKind == JsonValueKind.Array
            && captures.GetArrayLength() > 0)
        {
            var capture = captures[0];
            var captureId = GetString(capture, "id") ?? throw new PaymentGatewayException("PayPal did not return a capture id.");
            var captureStatus = GetString(capture, "status") ?? orderStatus;
            if (!string.Equals(captureStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                throw new PaymentGatewayException($"Payment was not completed (status: {captureStatus}).");
            return new PaymentResult(orderId, captureId, captureStatus);
        }

        throw new PaymentGatewayException($"Payment was not captured (order status: {orderStatus}).");
    }

    private static VaultedCard ReadVaultedCard(JsonElement root, CardDetails fallback)
    {
        var vaultId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal did not return a vault token id.");
        string? customerId = null;
        if (root.TryGetProperty("customer", out var customer))
            customerId = GetString(customer, "id");

        string brand = "CARD";
        string last4 = fallback.Number.Length >= 4 ? fallback.Number[^4..] : fallback.Number;
        int expiryMonth = fallback.ExpiryMonth;
        int expiryYear = fallback.ExpiryYear;
        string name = fallback.CardholderName;

        if (root.TryGetProperty("payment_source", out var source) && source.TryGetProperty("card", out var card))
        {
            brand = GetString(card, "brand") ?? brand;
            last4 = GetString(card, "last_digits") ?? last4;
            name = GetString(card, "name") ?? name;
            var expiry = GetString(card, "expiry"); // "YYYY-MM"
            if (expiry is not null && expiry.Length == 7 && expiry[4] == '-'
                && int.TryParse(expiry.AsSpan(0, 4), out var y) && int.TryParse(expiry.AsSpan(5, 2), out var m))
            {
                expiryYear = y;
                expiryMonth = m;
            }
        }

        return new VaultedCard(vaultId, customerId, brand, last4, expiryMonth, expiryYear, name);
    }

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
