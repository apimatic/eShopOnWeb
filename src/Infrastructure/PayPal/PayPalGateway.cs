using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/>, built directly against PayPal's OpenAPI
/// specs: Orders v2 (create + capture a card payment), Payments v2 (refund a capture) and Vault v3
/// (save/delete a card). Card data is only forwarded to PayPal; it is never persisted or logged here.
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(
        IHttpClientFactory httpClientFactory,
        PayPalAccessTokenProvider tokenProvider,
        IAppLogger<PayPalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task<PaymentResult> ChargeAsync(ChargeCardRequest request, CancellationToken cancellationToken = default)
    {
        // Build payment_source.card: either a one-off card, or a previously vaulted card via vault_id.
        object cardNode;
        if (!string.IsNullOrEmpty(request.VaultTokenId))
        {
            cardNode = new { vault_id = request.VaultTokenId };
        }
        else
        {
            var card = request.Card!;
            cardNode = new
            {
                number = card.Number,
                expiry = card.Expiry,
                security_code = card.SecurityCode,
                name = card.Name,
                billing_address = new
                {
                    address_line_1 = card.BillingAddress.AddressLine1,
                    address_line_2 = card.BillingAddress.AddressLine2,
                    admin_area_1 = card.BillingAddress.AdminArea1,
                    admin_area_2 = card.BillingAddress.AdminArea2,
                    postal_code = card.BillingAddress.PostalCode,
                    country_code = card.BillingAddress.CountryCode
                }
            };
        }

        var payload = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    amount = new
                    {
                        currency_code = request.CurrencyCode,
                        value = request.Amount.ToString("0.00", CultureInfo.InvariantCulture)
                    }
                }
            },
            payment_source = new { card = cardNode }
        };

        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = request.IdempotencyKey,
            ["Prefer"] = "return=representation"
        };

        using var response = await SendAsync(
            HttpMethod.Post, "/v2/checkout/orders", payload, headers, "create order", cancellationToken);

        var root = response.RootElement;
        var payPalOrderId = GetString(root, "id")
            ?? throw new PaymentGatewayException("PayPal did not return an order id.");
        var orderStatus = GetString(root, "status");

        var capture = TryFindCapture(root);

        // Single-step card orders (intent=CAPTURE + card) normally capture inline. If PayPal instead
        // returns an APPROVED order without a capture, complete it explicitly.
        if (capture is null && string.Equals(orderStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            capture = await CaptureOrderAsync(payPalOrderId, request.IdempotencyKey, cancellationToken);
        }

        if (capture is null)
        {
            throw new PaymentGatewayException(
                $"PayPal order {payPalOrderId} did not yield a capture (order status: {orderStatus}).",
                errorName: orderStatus);
        }

        return new PaymentResult(payPalOrderId, capture.Value.Id, capture.Value.Status);
    }

    private async Task<(string Id, string Status)?> CaptureOrderAsync(
        string payPalOrderId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = $"{idempotencyKey}-cap",
            ["Prefer"] = "return=representation"
        };

        using var response = await SendAsync(
            HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/capture",
            payload: null, headers, "capture order", cancellationToken);

        return TryFindCapture(response.RootElement);
    }

    public async Task<RefundResult> RefundAsync(
        string captureId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        // Empty body = full refund per the spec.
        using var response = await SendAsync(
            HttpMethod.Post, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            payload: new { }, headers, "refund capture", cancellationToken);

        var root = response.RootElement;
        var refundId = GetString(root, "id")
            ?? throw new PaymentGatewayException("PayPal did not return a refund id.");
        var status = GetString(root, "status") ?? "UNKNOWN";

        return new RefundResult(refundId, status);
    }

    public async Task<VaultedCard> VaultCardAsync(
        CardDetails card, string buyerReference, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            customer = new { id = DeriveCustomerId(buyerReference) },
            payment_source = new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.Name,
                    billing_address = new
                    {
                        address_line_1 = card.BillingAddress.AddressLine1,
                        address_line_2 = card.BillingAddress.AddressLine2,
                        admin_area_1 = card.BillingAddress.AdminArea1,
                        admin_area_2 = card.BillingAddress.AdminArea2,
                        postal_code = card.BillingAddress.PostalCode,
                        country_code = card.BillingAddress.CountryCode
                    }
                }
            }
        };

        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey
        };

        using var response = await SendAsync(
            HttpMethod.Post, "/v3/vault/payment-tokens", payload, headers, "vault card", cancellationToken);

        var root = response.RootElement;
        var tokenId = GetString(root, "id")
            ?? throw new PaymentGatewayException("PayPal did not return a vault token id.");

        string? brand = null, last4 = null, expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var ps) &&
            ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand");
            last4 = GetString(cardEl, "last_digits");
            expiry = GetString(cardEl, "expiry");
            name = GetString(cardEl, "name");
        }

        return new VaultedCard(tokenId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttpClient.Name);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultTokenId)}");
        await AddAuthorizationAsync(request, cancellationToken);

        using var response = await client.SendAsync(request, cancellationToken);

        // 204 = deleted. Treat an already-absent token (404) as success so delete is idempotent.
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw BuildException(response.StatusCode, body, "delete vault token");
    }

    // ---- HTTP plumbing -------------------------------------------------------------------------

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string path,
        object? payload,
        IDictionary<string, string> headers,
        string operation,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttpClient.Name);

        using var request = new HttpRequestMessage(method, path);
        await AddAuthorizationAsync(request, cancellationToken);

        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (payload is not null)
        {
            var json = JsonSerializer.Serialize(payload, SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, body, operation);
        }

        // Success responses for these operations are JSON objects; guard empty bodies defensively.
        if (string.IsNullOrWhiteSpace(body))
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(body);
    }

    private async Task AddAuthorizationAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>Parses PayPal's error model (name/message/debug_id) into a <see cref="PaymentGatewayException"/>.</summary>
    private PaymentGatewayException BuildException(HttpStatusCode statusCode, string body, string operation)
    {
        string? name = null, message = null, debugId = null, details = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message");
            debugId = GetString(root, "debug_id");

            if (root.TryGetProperty("details", out var det) && det.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var d in det.EnumerateArray())
                {
                    var issue = GetString(d, "issue");
                    var description = GetString(d, "description");
                    if (!string.IsNullOrEmpty(issue) || !string.IsNullOrEmpty(description))
                    {
                        parts.Add($"{issue}: {description}".Trim(':', ' '));
                    }
                }
                if (parts.Count > 0) details = string.Join("; ", parts);
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall back to the status code only.
        }

        var summary = message ?? name ?? $"PayPal request failed with status {(int)statusCode}.";
        if (!string.IsNullOrEmpty(details)) summary = $"{summary} ({details})";

        // Log only the safe error fields — never the request/response body (it may echo card material).
        _logger.LogWarning(
            $"PayPal {operation} failed: status {(int)statusCode}, name {name ?? "-"}, debug_id {debugId ?? "-"}.");

        return new PaymentGatewayException(
            $"PayPal {operation} failed: {summary}",
            httpStatusCode: (int)statusCode,
            errorName: name,
            debugId: debugId);
    }

    private static (string Id, string Status)? TryFindCapture(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var purchaseUnits) ||
            purchaseUnits.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var purchaseUnit in purchaseUnits.EnumerateArray())
        {
            if (!purchaseUnit.TryGetProperty("payments", out var payments) ||
                !payments.TryGetProperty("captures", out var captures) ||
                captures.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var capture in captures.EnumerateArray())
            {
                var id = GetString(capture, "id");
                if (string.IsNullOrEmpty(id)) continue;
                var status = GetString(capture, "status") ?? "UNKNOWN";
                return (id, status);
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Derives a stable, PayPal-safe customer id from the shopper's identity. PayPal's customer id is
    /// constrained (max 22 chars, <c>[0-9a-zA-Z_-]</c>), so the raw username (an email) can't be used;
    /// a deterministic hash keeps the same shopper mapped to the same PayPal customer across restarts.
    /// </summary>
    private static string DeriveCustomerId(string buyerReference)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerReference));
        return "c" + Convert.ToHexString(hash).Substring(0, 20).ToLowerInvariant();
    }
}
