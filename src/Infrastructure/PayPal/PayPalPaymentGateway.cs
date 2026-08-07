using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPayPalPaymentGateway"/>, built directly against the PayPal
/// OpenAPI specs: Orders v2 (create + capture), Payments v2 (refund) and Payment Method Tokens v3
/// (vault). Card data is sent only to PayPal and is never persisted or logged.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private readonly HttpClient _httpClient;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    public PayPalPaymentGateway(HttpClient httpClient, PayPalAccessTokenProvider tokenProvider,
        PayPalSettings settings, ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _settings = settings;
        _logger = logger;
    }

    public Task<CardChargeResult> ChargeWithCardAsync(decimal amount, string currencyCode, CardDetails card,
        string idempotencyKey, string? invoiceId = null, CancellationToken cancellationToken = default)
    {
        var cardJson = BuildCardJson(card);
        return CreateAndCaptureAsync(amount, currencyCode, cardJson, idempotencyKey, invoiceId, cancellationToken);
    }

    public Task<CardChargeResult> ChargeWithVaultedCardAsync(decimal amount, string currencyCode, string vaultId,
        string idempotencyKey, string? invoiceId = null, CancellationToken cancellationToken = default)
    {
        var cardJson = new JsonObject { ["vault_id"] = vaultId };
        return CreateAndCaptureAsync(amount, currencyCode, cardJson, idempotencyKey, invoiceId, cancellationToken);
    }

    private async Task<CardChargeResult> CreateAndCaptureAsync(decimal amount, string currencyCode, JsonObject cardJson,
        string idempotencyKey, string? invoiceId, CancellationToken cancellationToken)
    {
        var purchaseUnit = new JsonObject
        {
            ["amount"] = new JsonObject
            {
                ["currency_code"] = currencyCode,
                ["value"] = FormatAmount(amount)
            }
        };
        if (!string.IsNullOrWhiteSpace(invoiceId))
        {
            purchaseUnit["invoice_id"] = invoiceId;
        }

        var createBody = new JsonObject
        {
            ["intent"] = "CAPTURE",
            ["purchase_units"] = new JsonArray(purchaseUnit),
            ["payment_source"] = new JsonObject { ["card"] = cardJson }
        };

        // PayPal-Request-Id makes create idempotent; Prefer return=representation gives the full order back.
        var created = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", createBody, cancellationToken,
            requestId: idempotencyKey, preferRepresentation: true);

        var order = created!.AsObject();
        var payPalOrderId = order["id"]!.GetValue<string>();
        var status = order["status"]?.GetValue<string>() ?? "UNKNOWN";

        // If the order already carries a capture (already COMPLETED), use it; otherwise capture now.
        var capture = TryGetCapture(order);
        if (capture is null)
        {
            capture = await CaptureAsync(payPalOrderId, idempotencyKey, cancellationToken);
        }

        var captureId = capture["id"]!.GetValue<string>();
        var captureStatus = capture["status"]?.GetValue<string>() ?? "UNKNOWN";
        var (last4, brand) = ReadCardSummary(order);

        _logger.LogInformation(
            "PayPal order {OrderId} captured: capture {CaptureId} status {Status} (card ****{Last4}).",
            payPalOrderId, captureId, captureStatus, last4 ?? "????");

        if (!string.Equals(captureStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalApiException(HttpStatusCode.PaymentRequired, "CAPTURE_NOT_COMPLETED",
                $"PayPal capture {captureId} returned status '{captureStatus}'.", null);
        }

        return new CardChargeResult(payPalOrderId, captureId, captureStatus, last4, brand);
    }

    private async Task<JsonObject> CaptureAsync(string payPalOrderId, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            var captured = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/capture",
                new JsonObject(), cancellationToken, requestId: idempotencyKey + "-capture", preferRepresentation: true);
            var capture = TryGetCapture(captured!.AsObject());
            if (capture is not null)
            {
                return capture;
            }
        }
        catch (PayPalApiException ex) when (ex.PayPalErrorName == "ORDER_ALREADY_CAPTURED")
        {
            _logger.LogInformation("PayPal order {OrderId} was already captured; fetching existing capture.", payPalOrderId);
        }

        // Fallback: fetch the order and read the capture that must exist by now.
        var fetched = await SendAsync(HttpMethod.Get, $"/v2/checkout/orders/{payPalOrderId}", null, cancellationToken);
        var existing = TryGetCapture(fetched!.AsObject());
        if (existing is null)
        {
            throw new PayPalApiException(HttpStatusCode.BadGateway, "NO_CAPTURE",
                $"PayPal order {payPalOrderId} has no capture after processing.", null);
        }
        return existing;
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Full refund: an empty body refunds the entire captured amount (partial refunds are out of scope).
        var refund = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            new JsonObject(), cancellationToken, requestId: idempotencyKey, preferRepresentation: true);

        var obj = refund!.AsObject();
        var refundId = obj["id"]!.GetValue<string>();
        var status = obj["status"]?.GetValue<string>() ?? "UNKNOWN";

        _logger.LogInformation("PayPal refund {RefundId} for capture {CaptureId} returned status {Status}.",
            refundId, captureId, status);

        if (status is not ("COMPLETED" or "PENDING"))
        {
            throw new PayPalApiException(HttpStatusCode.BadGateway, "REFUND_NOT_COMPLETED",
                $"PayPal refund {refundId} returned status '{status}'.", null);
        }

        return new RefundResult(refundId, status);
    }

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = BuildCardJson(card) }
        };

        var response = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, cancellationToken,
            preferRepresentation: true);

        var obj = response!.AsObject();
        var vaultId = obj["id"]!.GetValue<string>();

        var cardResponse = obj["payment_source"]?["card"]?.AsObject();
        var last4 = cardResponse?["last_digits"]?.GetValue<string>();
        var brand = cardResponse?["brand"]?.GetValue<string>();
        var expiry = cardResponse?["expiry"]?.GetValue<string>();

        // Fall back to what we know locally if the vault response omits the descriptor.
        last4 ??= card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
        expiry ??= card.ExpiryMonthYear;

        _logger.LogInformation("Vaulted card token {VaultId} created (card ****{Last4}).", vaultId, last4);
        return new VaultedCard(vaultId, last4!, brand, expiry, card.CardholderName);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, cancellationToken);
            _logger.LogInformation("Deleted vaulted card token {VaultId}.", vaultId);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone at PayPal — the desired end state is achieved.
            _logger.LogInformation("Vaulted card token {VaultId} was already absent at PayPal.", vaultId);
        }
    }

    // ---- helpers ----

    private static JsonObject BuildCardJson(CardDetails card)
    {
        var json = new JsonObject
        {
            ["name"] = card.CardholderName,
            ["number"] = card.Number,
            ["expiry"] = card.ExpiryMonthYear,
            ["security_code"] = card.SecurityCode
        };

        if (card.BillingAddress is { } b)
        {
            var address = new JsonObject();
            if (!string.IsNullOrWhiteSpace(b.AddressLine1)) address["address_line_1"] = b.AddressLine1;
            if (!string.IsNullOrWhiteSpace(b.AddressLine2)) address["address_line_2"] = b.AddressLine2;
            if (!string.IsNullOrWhiteSpace(b.City)) address["admin_area_2"] = b.City;
            if (!string.IsNullOrWhiteSpace(b.State)) address["admin_area_1"] = b.State;
            if (!string.IsNullOrWhiteSpace(b.PostalCode)) address["postal_code"] = b.PostalCode;
            if (!string.IsNullOrWhiteSpace(b.CountryCode)) address["country_code"] = b.CountryCode;
            if (address.Count > 0) json["billing_address"] = address;
        }

        return json;
    }

    private static JsonObject? TryGetCapture(JsonObject order)
    {
        var captures = order["purchase_units"]?.AsArray();
        if (captures is null) return null;
        foreach (var pu in captures)
        {
            var caps = pu?["payments"]?["captures"]?.AsArray();
            if (caps is { Count: > 0 })
            {
                return caps[0]!.AsObject();
            }
        }
        return null;
    }

    private static (string? Last4, string? Brand) ReadCardSummary(JsonObject order)
    {
        var card = order["payment_source"]?["card"]?.AsObject();
        if (card is null) return (null, null);
        var last4 = card["last_digits"]?.GetValue<string>();
        var brand = card["brand"]?.GetValue<string>();
        return (last4, brand);
    }

    private static string FormatAmount(decimal amount)
        => Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Sends a request to PayPal with a bearer token, retrying once on 401 with a refreshed token.
    /// Returns the parsed JSON body, or null for empty (204) responses. Throws
    /// <see cref="PayPalApiException"/> for non-success responses.
    /// </summary>
    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body,
        CancellationToken cancellationToken, string? requestId = null, bool preferRepresentation = false)
    {
        var response = await SendOnceAsync(method, path, body, requestId, preferRepresentation, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _tokenProvider.Invalidate();
            response = await SendOnceAsync(method, path, body, requestId, preferRepresentation, cancellationToken);
        }

        using (response)
        {
            var content = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException(response.StatusCode, content, method, path);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            return JsonNode.Parse(content);
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, JsonObject? body,
        string? requestId, bool preferRepresentation, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        var request = new HttpRequestMessage(method, _settings.ResolveBaseUrl() + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            var payload = body.ToJsonString(SerializerOptions);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private PayPalApiException BuildApiException(HttpStatusCode statusCode, string content, HttpMethod method, string path)
    {
        string? name = null;
        string message = $"PayPal request {method} {path} failed with status {(int)statusCode}.";
        string? debugId = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (root.TryGetProperty("name", out var n)) name = n.GetString();
                if (root.TryGetProperty("debug_id", out var d)) debugId = d.GetString();
                var detail = name;
                if (root.TryGetProperty("message", out var m)) detail = $"{name}: {m.GetString()}";
                if (root.TryGetProperty("details", out var det) && det.ValueKind == JsonValueKind.Array && det.GetArrayLength() > 0)
                {
                    var first = det[0];
                    var issue = first.TryGetProperty("issue", out var iss) ? iss.GetString() : null;
                    var desc = first.TryGetProperty("description", out var ds) ? ds.GetString() : null;
                    if (issue is not null || desc is not null)
                    {
                        detail = $"{detail} ({issue}: {desc})";
                    }
                }
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    message = detail!;
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message.
        }

        _logger.LogError("PayPal API error on {Method} {Path}: {Status} {Name} (debug_id: {DebugId}).",
            method, path, (int)statusCode, name, debugId);

        return new PayPalApiException(statusCode, name, message, debugId);
    }
}
