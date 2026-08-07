using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal REST implementation of <see cref="IPaymentGateway"/>. Uses the Orders v2 API for card
/// charges and refunds, and the Payment Method Tokens v3 (Vault) API to save and reuse cards.
///
/// Confirmed against PayPal documentation:
///  - OAuth2:  POST /v1/oauth2/token (client_credentials)
///  - Charge:  POST /v2/checkout/orders (intent=CAPTURE, payment_source.card) + /capture
///  - Refund:  POST /v2/payments/captures/{id}/refund (empty body = full refund)
///  - Save:    POST /v3/vault/setup-tokens then POST /v3/vault/payment-tokens (SETUP_TOKEN)
///  - Reuse:   payment_source.card.vault_id on the order
///  - Delete:  DELETE /v3/vault/payment-tokens/{id}
///  - Idempotency: PayPal-Request-Id header
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly PayPalTokenCache _tokenCache;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalPaymentGateway(
        HttpClient httpClient,
        PayPalSettings settings,
        PayPalTokenCache tokenCache,
        ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _tokenCache = tokenCache;
        _logger = logger;
    }

    public async Task<GatewayChargeResult> ChargeCardAsync(
        decimal amount, string currencyCode, PaymentCard card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardNode = BuildRawCardNode(card);
        return await CreateAndCaptureOrderAsync(amount, currencyCode, cardNode, idempotencyKey, cancellationToken);
    }

    public async Task<GatewayChargeResult> ChargeVaultedCardAsync(
        decimal amount, string currencyCode, string vaultToken, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardNode = new Dictionary<string, object?> { ["vault_id"] = vaultToken };
        return await CreateAndCaptureOrderAsync(amount, currencyCode, cardNode, idempotencyKey, cancellationToken);
    }

    public async Task<GatewayRefundResult> RefundAsync(
        string captureId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Empty body => full refund of the capture.
        using var doc = await SendAsync(
            HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", "{}", idempotencyKey, cancellationToken);

        var root = doc.RootElement;
        var refundId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal refund response did not contain an id.");
        var status = GetString(root, "status") ?? "UNKNOWN";
        return new GatewayRefundResult(refundId, status);
    }

    public async Task<GatewaySavedCard> VaultCardAsync(
        PaymentCard card, string? customerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Step 1: create a setup token from the raw card.
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildRawCardNode(card) }
        };
        if (!string.IsNullOrEmpty(customerId))
        {
            setupBody["customer"] = new Dictionary<string, object?> { ["id"] = customerId };
        }

        string setupTokenId;
        using (var setupDoc = await SendAsync(
            HttpMethod.Post, "/v3/vault/setup-tokens", Serialize(setupBody), idempotencyKey + "-setup", cancellationToken))
        {
            setupTokenId = GetString(setupDoc.RootElement, "id")
                ?? throw new PaymentGatewayException("PayPal setup-token response did not contain an id.");
        }

        // Step 2: exchange the setup token for a durable payment (vault) token.
        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?> { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
            }
        };

        using var tokenDoc = await SendAsync(
            HttpMethod.Post, "/v3/vault/payment-tokens", Serialize(tokenBody), idempotencyKey + "-token", cancellationToken);

        var root = tokenDoc.RootElement;
        var vaultToken = GetString(root, "id")
            ?? throw new PaymentGatewayException("PayPal payment-token response did not contain an id.");

        string? resolvedCustomerId = null;
        if (root.TryGetProperty("customer", out var customerEl))
        {
            resolvedCustomerId = GetString(customerEl, "id");
        }

        string last4 = "0000", brand = "UNKNOWN", expiry = "";
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            last4 = GetString(cardEl, "last_digits") ?? last4;
            brand = GetString(cardEl, "brand") ?? brand;
            expiry = GetString(cardEl, "expiry") ?? card.ExpiryYearMonth;
        }
        if (string.IsNullOrEmpty(expiry))
        {
            expiry = card.ExpiryYearMonth;
        }

        _logger.LogInformation("Vaulted card ending {Last4} ({Brand}) as PayPal token.", last4, brand);
        return new GatewaySavedCard(vaultToken, last4, brand, expiry, resolvedCustomerId);
    }

    public async Task DeleteVaultedCardAsync(string vaultToken, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultToken}");
        await AuthorizeAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        // 204 => deleted. 404 => already gone; treat as success (idempotent delete).
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw BuildGatewayException("delete vaulted card", response.StatusCode, body);
    }

    // ----- internals -------------------------------------------------------------------------

    private async Task<GatewayChargeResult> CreateAndCaptureOrderAsync(
        decimal amount, string currencyCode, Dictionary<string, object?> cardNode, string idempotencyKey, CancellationToken cancellationToken)
    {
        var orderBody = new Dictionary<string, object?>
        {
            ["intent"] = "CAPTURE",
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = cardNode },
            ["purchase_units"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["amount"] = new Dictionary<string, object?>
                    {
                        ["currency_code"] = currencyCode,
                        ["value"] = amount.ToString("F2", CultureInfo.InvariantCulture)
                    }
                }
            }
        };

        string payPalOrderId;
        string? status;
        string? captureId;

        using (var createDoc = await SendAsync(
            HttpMethod.Post, "/v2/checkout/orders", Serialize(orderBody), idempotencyKey, cancellationToken))
        {
            var root = createDoc.RootElement;
            payPalOrderId = GetString(root, "id")
                ?? throw new PaymentGatewayException("PayPal create-order response did not contain an id.");
            status = GetString(root, "status");
            captureId = TryExtractCaptureId(root);
        }

        // With intent=CAPTURE and a card, PayPal may capture synchronously (status COMPLETED with the
        // capture already present). Otherwise, capture explicitly.
        if (!string.Equals(status, "COMPLETED", StringComparison.Ordinal) || captureId is null)
        {
            if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.Ordinal))
            {
                throw new PaymentGatewayException(
                    "PayPal requires additional payer authentication (e.g. 3-D Secure) for this card, " +
                    "which this server-to-server flow cannot complete.");
            }

            using var captureDoc = await SendAsync(
                HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/capture", "{}", idempotencyKey + "-capture", cancellationToken);

            var captureRoot = captureDoc.RootElement;
            status = GetString(captureRoot, "status") ?? status;
            captureId = TryExtractCaptureId(captureRoot);
        }

        if (captureId is null)
        {
            throw new PaymentGatewayException(
                $"PayPal did not return a capture for order {payPalOrderId} (status: {status ?? "unknown"}).");
        }

        _logger.LogInformation("PayPal order {OrderId} captured (capture {CaptureId}, status {Status}).",
            payPalOrderId, captureId, status);
        return new GatewayChargeResult(payPalOrderId, captureId, status ?? "COMPLETED");
    }

    private static Dictionary<string, object?> BuildRawCardNode(PaymentCard card)
    {
        var node = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.ExpiryYearMonth,
            ["security_code"] = card.SecurityCode
        };

        if (!string.IsNullOrWhiteSpace(card.CardholderName))
        {
            node["name"] = card.CardholderName;
        }

        if (card.BillingAddress is not null)
        {
            var b = card.BillingAddress;
            var address = new Dictionary<string, object?> { ["address_line_1"] = b.AddressLine1 };
            if (!string.IsNullOrWhiteSpace(b.AdminArea1)) address["admin_area_1"] = b.AdminArea1;
            if (!string.IsNullOrWhiteSpace(b.AdminArea2)) address["admin_area_2"] = b.AdminArea2;
            if (!string.IsNullOrWhiteSpace(b.PostalCode)) address["postal_code"] = b.PostalCode;
            if (!string.IsNullOrWhiteSpace(b.CountryCode)) address["country_code"] = b.CountryCode;
            node["billing_address"] = address;
        }

        return node;
    }

    private static string? TryExtractCaptureId(JsonElement root)
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
                        var id = GetString(capture, "id");
                        if (!string.IsNullOrEmpty(id))
                        {
                            return id;
                        }
                    }
                }
            }
        }
        return null;
    }

    /// <summary>Sends an authorized JSON request and returns the parsed response, or throws on failure.</summary>
    private async Task<JsonDocument> SendAsync(
        HttpMethod method, string path, string jsonBody, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        await AuthorizeAsync(request, cancellationToken);
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.Add("PayPal-Request-Id", idempotencyKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildGatewayException($"{method} {path}", response.StatusCode, body);
        }

        return string.IsNullOrWhiteSpace(body)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(body);
    }

    private async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenCache.GetAccessTokenAsync(FetchAccessTokenAsync, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<(string token, int expiresInSeconds)> FetchAccessTokenAsync(CancellationToken cancellationToken)
    {
        _settings.Validate();

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            })
        };

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Do not surface the raw body here (it may echo request specifics); give an actionable message.
            _logger.LogError("PayPal OAuth token request failed with status {Status}.", (int)response.StatusCode);
            throw new PaymentGatewayException(
                $"Failed to obtain a PayPal access token (HTTP {(int)response.StatusCode}). Check PayPal:ClientId/ClientSecret.");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var token = GetString(root, "access_token")
            ?? throw new PaymentGatewayException("PayPal OAuth response did not contain an access_token.");
        var expiresIn = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds) ? seconds : 300;

        _logger.LogInformation("Obtained PayPal access token (valid {Seconds}s).", expiresIn);
        return (token, expiresIn);
    }

    private static PaymentGatewayException BuildGatewayException(string operation, HttpStatusCode statusCode, string body)
    {
        string? name = null, message = null, debugId = null;
        var detailBuilder = new StringBuilder();

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message");
            debugId = GetString(root, "debug_id");

            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var issue = GetString(detail, "issue");
                    var description = GetString(detail, "description");
                    if (!string.IsNullOrEmpty(issue) || !string.IsNullOrEmpty(description))
                    {
                        if (detailBuilder.Length > 0) detailBuilder.Append("; ");
                        detailBuilder.Append(issue);
                        if (!string.IsNullOrEmpty(description)) detailBuilder.Append(" - ").Append(description);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall back to a generic message without echoing raw content.
        }

        var summary = message ?? name ?? "PayPal request failed";
        var full = $"PayPal {operation} failed (HTTP {(int)statusCode}): {summary}";
        if (detailBuilder.Length > 0)
        {
            full += $" [{detailBuilder}]";
        }

        return new PaymentGatewayException(full, debugId);
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
