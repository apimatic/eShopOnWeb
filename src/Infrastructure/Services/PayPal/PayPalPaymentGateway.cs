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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Talks to PayPal's REST API over HTTP for the capabilities this integration needs. All shapes follow
/// the verified PayPal v2 Orders / v2 Payments / v3 Vault contracts. Raw card data is only ever sent to
/// PayPal in the request body and is never logged.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private readonly HttpClient _httpClient;
    private readonly PayPalAccessTokenCache _tokenCache;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PayPalPaymentGateway(
        HttpClient httpClient,
        PayPalAccessTokenCache tokenCache,
        IOptions<PayPalSettings> settings,
        ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _tokenCache = tokenCache;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PayPalPaymentResult> ChargeCardAsync(decimal amount, string currency,
        PayPalCardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var card_ = BuildCardNode(card);
        return await CreateAndReadOrderAsync(amount, currency, card_, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PayPalPaymentResult> ChargeVaultedCardAsync(decimal amount, string currency,
        string vaultId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var card_ = new Dictionary<string, object?> { ["vault_id"] = vaultId };
        return await CreateAndReadOrderAsync(amount, currency, card_, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PayPalPaymentResult> CreateAndReadOrderAsync(decimal amount, string currency,
        Dictionary<string, object?> cardNode, string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "CAPTURE",
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = cardNode },
            ["purchase_units"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["amount"] = new Dictionary<string, object?>
                    {
                        ["currency_code"] = currency,
                        ["value"] = FormatAmount(amount)
                    }
                }
            }
        };

        using var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            idempotencyKey, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // A declined card comes back as 422 UNPROCESSABLE_ENTITY - a business outcome, not an error.
        if (response.StatusCode == (HttpStatusCode)422)
        {
            var reason = ExtractIssue(content) ?? "Card payment was declined.";
            _logger.LogWarning("PayPal order declined (debug_id={DebugId}): {Reason}", DebugId(response), reason);
            return new PayPalPaymentResult(false, "DECLINED", null, null, null, null, reason);
        }

        EnsureSuccess(response, content);

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var status = GetString(root, "status") ?? "UNKNOWN";
        var orderId = GetString(root, "id");

        string? captureId = null;
        string? captureStatus = null;
        if (TryGetCapture(root, out var capture))
        {
            captureId = GetString(capture, "id");
            captureStatus = GetString(capture, "status");
        }

        var (brand, last4) = ReadCardEcho(root);

        var succeeded = string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            && string.Equals(captureStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(captureId);

        if (!succeeded)
        {
            _logger.LogWarning("PayPal order {OrderId} did not complete (status={Status}, captureStatus={CaptureStatus}).",
                orderId, status, captureStatus);
            return new PayPalPaymentResult(false, status, orderId, captureId, brand, last4,
                $"Payment not completed (order status {status}).");
        }

        return new PayPalPaymentResult(true, status, orderId, captureId, brand, last4, null);
    }

    public async Task<PayPalVaultResult> VaultCardAsync(PayPalCardDetails card, string? payPalCustomerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Step 1: create a setup token from the raw card.
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCardNode(card) }
        };
        if (!string.IsNullOrEmpty(payPalCustomerId))
        {
            setupBody["customer"] = new Dictionary<string, object?> { ["id"] = payPalCustomerId };
        }

        string setupTokenId;
        using (var setupResponse = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody,
                   idempotencyKey + "-setup", cancellationToken).ConfigureAwait(false))
        {
            var setupContent = await setupResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (setupResponse.StatusCode == (HttpStatusCode)422)
            {
                var reason = ExtractIssue(setupContent) ?? "The card could not be saved.";
                throw new PayPalApiException(reason, setupResponse.StatusCode, DebugId(setupResponse), "SETUP_TOKEN_UNPROCESSABLE");
            }
            EnsureSuccess(setupResponse, setupContent);
            using var setupDoc = JsonDocument.Parse(setupContent);
            setupTokenId = GetString(setupDoc.RootElement, "id")
                ?? throw new PayPalApiException("PayPal setup token response missing id.",
                    setupResponse.StatusCode, DebugId(setupResponse), null);
        }

        // Step 2: exchange the setup token for a permanent payment (vault) token.
        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?>
                {
                    ["id"] = setupTokenId,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        using var tokenResponse = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody,
            idempotencyKey + "-token", cancellationToken).ConfigureAwait(false);
        var tokenContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(tokenResponse, tokenContent);

        using var tokenDoc = JsonDocument.Parse(tokenContent);
        var tokenRoot = tokenDoc.RootElement;
        var vaultId = GetString(tokenRoot, "id")
            ?? throw new PayPalApiException("PayPal payment token response missing id.",
                tokenResponse.StatusCode, DebugId(tokenResponse), null);

        var (brand, last4) = ReadCardEcho(tokenRoot);
        var expiry = card.Expiry;
        var name = card.CardholderName;
        string? customerId = null;
        if (tokenRoot.TryGetProperty("customer", out var customer))
        {
            customerId = GetString(customer, "id");
        }

        // Prefer the safe values echoed by PayPal where present.
        if (tokenRoot.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEcho))
        {
            expiry = GetString(cardEcho, "expiry") ?? expiry;
            name = GetString(cardEcho, "name") ?? name;
        }

        return new PayPalVaultResult(
            vaultId,
            brand ?? "CARD",
            last4 ?? Last4Of(card.Number),
            expiry,
            name,
            customerId ?? payPalCustomerId ?? string.Empty);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // Empty body => full refund.
        using var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", new Dictionary<string, object?>(),
            idempotencyKey, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == (HttpStatusCode)422)
        {
            var reason = ExtractIssue(content) ?? "The refund could not be processed.";
            _logger.LogWarning("PayPal refund unprocessable (debug_id={DebugId}): {Reason}", DebugId(response), reason);
            return new PayPalRefundResult(false, "UNPROCESSABLE", null, reason);
        }

        EnsureSuccess(response, content);

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var status = GetString(root, "status") ?? "UNKNOWN";
        var refundId = GetString(root, "id");

        // A refund is COMPLETED immediately in sandbox; PENDING is also an accepted (non-failed) outcome.
        var succeeded = !string.IsNullOrEmpty(refundId)
            && (status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase)
                || status.Equals("PENDING", StringComparison.OrdinalIgnoreCase));

        return succeeded
            ? new PayPalRefundResult(true, status, refundId, null)
            : new PayPalRefundResult(false, status, refundId, $"Refund not completed (status {status}).");
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{vaultId}", body: null, idempotencyKey: null, cancellationToken)
            .ConfigureAwait(false);

        // 204 No Content on success; 404 means it's already gone - both are fine for our purposes.
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, content);
    }

    // --- HTTP plumbing --------------------------------------------------------------------

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        var response = await SendOnceAsync(method, path, body, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        // Token could have been revoked/expired; refresh once and retry.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _tokenCache.Invalidate();
            response = await SendOnceAsync(method, path, body, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        return await _tokenCache.GetTokenAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new PayPalApiException(
                    "PayPal ClientId/ClientSecret are not configured. Set PayPal:ClientId and PayPal:ClientSecret.",
                    HttpStatusCode.Unauthorized, null, "CONFIGURATION_MISSING");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                })
            };
            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new PayPalApiException(
                    "Failed to obtain a PayPal access token. Check PayPal:ClientId / PayPal:ClientSecret.",
                    response.StatusCode, DebugId(response), "TOKEN_REQUEST_FAILED");
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var accessToken = GetString(root, "access_token")
                ?? throw new PayPalApiException("PayPal token response missing access_token.",
                    response.StatusCode, DebugId(response), null);
            var expiresIn = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds)
                ? seconds
                : 3600;
            return (accessToken, expiresIn);
        }, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureSuccess(HttpResponseMessage response, string content)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? name = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            name = GetString(doc.RootElement, "name");
            message = GetString(doc.RootElement, "message");
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall back to status.
        }

        var debugId = DebugId(response);
        _logger.LogError("PayPal API call failed: {Status} {Name} {Message} (debug_id={DebugId})",
            (int)response.StatusCode, name, message, debugId);
        throw new PayPalApiException(
            message ?? $"PayPal API call failed with status {(int)response.StatusCode}.",
            response.StatusCode, debugId, name);
    }

    // --- Request/response helpers --------------------------------------------------------

    private static Dictionary<string, object?> BuildCardNode(PayPalCardDetails card)
    {
        var node = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["name"] = card.CardholderName
        };
        if (!string.IsNullOrEmpty(card.SecurityCode))
        {
            node["security_code"] = card.SecurityCode;
        }

        var address = card.BillingAddress;
        if (address != null)
        {
            var billing = new Dictionary<string, object?>();
            AddIfPresent(billing, "address_line_1", address.AddressLine1);
            AddIfPresent(billing, "admin_area_2", address.AdminArea2);
            AddIfPresent(billing, "admin_area_1", address.AdminArea1);
            AddIfPresent(billing, "postal_code", address.PostalCode);
            AddIfPresent(billing, "country_code", address.CountryCode);
            if (billing.Count > 0)
            {
                node["billing_address"] = billing;
            }
        }

        return node;
    }

    private static void AddIfPresent(IDictionary<string, object?> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }

    private static bool TryGetCapture(JsonElement root, out JsonElement capture)
    {
        capture = default;
        if (root.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.TryGetProperty("payments", out var payments)
                    && payments.TryGetProperty("captures", out var captures)
                    && captures.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in captures.EnumerateArray())
                    {
                        capture = c;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static (string? brand, string? last4) ReadCardEcho(JsonElement root)
    {
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            return (GetString(card, "brand"), GetString(card, "last_digits"));
        }
        return (null, null);
    }

    private static string? ExtractIssue(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    var issue = GetString(d, "issue");
                    var description = GetString(d, "description");
                    if (!string.IsNullOrEmpty(issue) || !string.IsNullOrEmpty(description))
                    {
                        return description is null ? issue : $"{issue}: {description}";
                    }
                }
            }
            return GetString(root, "message");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? DebugId(HttpResponseMessage response)
        => response.Headers.TryGetValues("PayPal-Debug-Id", out var values)
            ? string.Join(",", values)
            : null;

    private static string FormatAmount(decimal amount)
        => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Last4Of(string number)
        => number.Length >= 4 ? number.Substring(number.Length - 4) : number;
}
