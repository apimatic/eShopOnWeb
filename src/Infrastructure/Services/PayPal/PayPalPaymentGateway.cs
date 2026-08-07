using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal REST implementation of <see cref="IPaymentGateway"/>. Talks to the PayPal Orders v2,
/// Payments v2 and Payment Method Tokens v3 APIs over plain HTTP.
///
/// Security notes:
///  - Raw card data is only ever sent to PayPal over TLS; it is never persisted or logged.
///  - Only PayPal's own resource ids and a safe card summary (brand / last 4 / expiry) come back.
///  - Every mutating call sends a caller-supplied <c>PayPal-Request-Id</c> for idempotency so a
///    retry or double-click cannot produce a duplicate charge, vault or refund.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPayPalAccessTokenProvider _tokenProvider;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(
        IHttpClientFactory httpClientFactory,
        IPayPalAccessTokenProvider tokenProvider,
        ILogger<PayPalPaymentGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task<CardChargeResult> ChargeCardAsync(
        decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            intent = "CAPTURE",
            payment_source = new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.CardholderName,
                    billing_address = ToBillingAddress(card.BillingAddress),
                },
            },
            purchase_units = new[] { new { amount = ToAmount(amount, currency) } },
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", payload, idempotencyKey, cancellationToken);
        return ReadChargeResult(doc.RootElement);
    }

    public async Task<CardChargeResult> ChargeVaultedCardAsync(
        decimal amount, string currency, string vaultId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            intent = "CAPTURE",
            payment_source = new { card = new { vault_id = vaultId } },
            purchase_units = new[] { new { amount = ToAmount(amount, currency) } },
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", payload, idempotencyKey, cancellationToken);
        return ReadChargeResult(doc.RootElement);
    }

    public async Task<VaultedCardResult> VaultCardAsync(
        CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Step 1: create a setup token from the raw card. For a raw card this returns APPROVED
        // immediately (no buyer approval redirect needed).
        var setupPayload = new
        {
            payment_source = new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    name = card.CardholderName,
                    billing_address = ToBillingAddress(card.BillingAddress),
                },
            },
        };

        string setupTokenId;
        using (var setupDoc = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupPayload, idempotencyKey, cancellationToken))
        {
            setupTokenId = setupDoc.RootElement.GetProperty("id").GetString()
                ?? throw new PaymentGatewayException("PayPal did not return a setup-token id.");
        }

        // Step 2: exchange the approved setup token for a permanent payment token (the vault id).
        var tokenPayload = new
        {
            payment_source = new { token = new { id = setupTokenId, type = "SETUP_TOKEN" } },
        };

        using var tokenDoc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenPayload, idempotencyKey + "-pt", cancellationToken);
        var root = tokenDoc.RootElement;

        var vaultId = root.GetProperty("id").GetString()
            ?? throw new PaymentGatewayException("PayPal did not return a payment-token id.");

        var (last4, brand, expiry) = ReadCardSummary(root);
        return new VaultedCardResult(vaultId, brand ?? "CARD", last4 ?? "0000", expiry ?? card.Expiry);
    }

    public async Task<RefundResult> RefundCaptureAsync(
        string captureId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Empty body = full refund of the capture.
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", new { }, idempotencyKey, cancellationToken);
        var root = doc.RootElement;

        var refundId = root.GetProperty("id").GetString()
            ?? throw new PaymentGatewayException("PayPal did not return a refund id.");
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "UNKNOWN" : "UNKNOWN";

        return new RefundResult(refundId, status);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", body: null, idempotencyKey: null, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best effort only: the card is already unusable once removed from our own store.
            _logger.LogWarning(ex, "Best-effort deletion of PayPal vault token failed.");
        }
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttpClient.Name);
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Ask PayPal to return the full resource so capture ids / card summaries are inline.
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, responseBody);
        }

        // Some successful responses (e.g. DELETE 204) have no body.
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(responseBody);
    }

    private PaymentGatewayException BuildException(HttpStatusCode statusCode, string responseBody)
    {
        string? debugId = null;
        string message = $"PayPal request failed (HTTP {(int)statusCode}).";

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("debug_id", out var dbg))
            {
                debugId = dbg.GetString();
            }

            // Prefer the most specific human-readable description PayPal provides, without echoing
            // back any of the request (card) data.
            string? name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? topMessage = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            string? detail = null;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
            {
                var first = details[0];
                var issue = first.TryGetProperty("issue", out var iss) ? iss.GetString() : null;
                var description = first.TryGetProperty("description", out var d) ? d.GetString() : null;
                detail = string.Join(": ", new[] { issue, description }.Where(x => !string.IsNullOrEmpty(x)));
            }

            var composed = string.Join(" - ", new[] { name, topMessage, detail }.Where(x => !string.IsNullOrEmpty(x)));
            if (!string.IsNullOrEmpty(composed))
            {
                message = $"PayPal request failed (HTTP {(int)statusCode}): {composed}";
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message.
        }

        _logger.LogError("PayPal error. Status={Status} DebugId={DebugId}", (int)statusCode, debugId);
        return new PaymentGatewayException(message, debugId);
    }

    private static CardChargeResult ReadChargeResult(JsonElement root)
    {
        var orderId = root.GetProperty("id").GetString()
            ?? throw new PaymentGatewayException("PayPal did not return an order id.");
        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "UNKNOWN" : "UNKNOWN";

        var captureId = TryFindCaptureId(root);
        if (status != "COMPLETED" || captureId is null)
        {
            throw new PaymentGatewayException(
                $"Card payment was not completed by PayPal (status: {status}).");
        }

        var (last4, brand, _) = ReadCardSummary(root);
        return new CardChargeResult(orderId, captureId, status, last4, brand);
    }

    private static string? TryFindCaptureId(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty("captures", out var captures) &&
                captures.ValueKind == JsonValueKind.Array &&
                captures.GetArrayLength() > 0)
            {
                return captures[0].GetProperty("id").GetString();
            }
        }

        return null;
    }

    private static (string? last4, string? brand, string? expiry) ReadCardSummary(JsonElement root)
    {
        if (root.TryGetProperty("payment_source", out var ps) &&
            ps.TryGetProperty("card", out var card))
        {
            string? last4 = card.TryGetProperty("last_digits", out var ld) ? ld.GetString() : null;
            string? brand = card.TryGetProperty("brand", out var br) ? br.GetString() : null;
            string? expiry = card.TryGetProperty("expiry", out var ex) ? ex.GetString() : null;
            return (last4, brand, expiry);
        }

        return (null, null, null);
    }

    private static object ToAmount(decimal amount, string currency) => new
    {
        currency_code = currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture),
    };

    private static object ToBillingAddress(CardBillingAddress a) => new
    {
        address_line_1 = a.AddressLine1,
        address_line_2 = a.AddressLine2,
        admin_area_2 = a.AdminArea2,
        admin_area_1 = a.AdminArea1,
        postal_code = a.PostalCode,
        country_code = a.CountryCode,
    };
}
