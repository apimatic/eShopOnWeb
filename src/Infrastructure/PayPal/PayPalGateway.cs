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
using System.Web;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// HTTP implementation of <see cref="IPayPalGateway"/> against the PayPal REST API.
/// No SDK — plain HTTPS. Card details are forwarded to PayPal and never persisted or logged.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly string _baseUrl;

    private static readonly SemaphoreSlim _tokenLock = new(1, 1);
    private static string? _cachedToken;
    private static DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalGateway(HttpClient http, IOptions<PayPalSettings> settings, ILogger<PayPalGateway> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
        _baseUrl = _settings.ResolveBaseUrl();
    }

    public string Currency => _settings.EffectiveCurrency;

    // ---------------------------------------------------------------------
    // OAuth2
    // ---------------------------------------------------------------------
    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _cachedToken;
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new PayPalApiException("PayPal credentials are not configured (PayPal:ClientId / PayPal:ClientSecret).", 500);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw ToApiException(response.StatusCode, body, response, "obtain access token");
            }

            var node = JsonNode.Parse(body)!;
            var token = node["access_token"]!.GetValue<string>();
            var expiresIn = node["expires_in"]?.GetValue<int>() ?? 3000;
            _cachedToken = token;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // ---------------------------------------------------------------------
    // Core send helper
    // ---------------------------------------------------------------------
    private async Task<JsonNode?> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string operation,
        CancellationToken ct,
        string? requestId = null,
        bool preferRepresentation = true)
    {
        var token = await GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw ToApiException(response.StatusCode, responseBody, response, operation);
        }

        return string.IsNullOrWhiteSpace(responseBody) ? null : JsonNode.Parse(responseBody);
    }

    private PayPalApiException ToApiException(HttpStatusCode status, string body, HttpResponseMessage response, string operation)
    {
        string? issue = null;
        string? name = null;
        string? message = null;
        string? debugId = null;
        try
        {
            var node = string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
            name = node?["name"]?.GetValue<string>();
            message = node?["message"]?.GetValue<string>();
            debugId = node?["debug_id"]?.GetValue<string>();
            var details = node?["details"] as JsonArray;
            issue = (details?.FirstOrDefault())?["issue"]?.GetValue<string>();
        }
        catch { /* non-JSON error body */ }

        if (response.Headers.TryGetValues("PayPal-Debug-Id", out var dbg))
        {
            debugId ??= dbg.FirstOrDefault();
        }

        var summary = $"PayPal failed to {operation}: {(int)status} {name ?? status.ToString()}"
                    + (issue is not null ? $" ({issue})" : string.Empty)
                    + (message is not null ? $" - {message}" : string.Empty)
                    + (debugId is not null ? $" [debug_id={debugId}]" : string.Empty);
        _logger.LogWarning("PayPal API error while trying to {Operation}: {Status} {Issue} debug_id={DebugId}",
            operation, (int)status, issue ?? name, debugId);
        return new PayPalApiException(summary, (int)status, issue ?? name, debugId);
    }

    // ---------------------------------------------------------------------
    // Authorize
    // ---------------------------------------------------------------------
    public async Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(PayPalMoney amount, CardDetails card, string requestId, CancellationToken ct = default)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[] { new { amount = new { currency_code = amount.CurrencyCode, value = amount.Value } } },
            payment_source = new { card = BuildCardObject(card) }
        };
        var node = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, "authorize card payment", ct, requestId);
        return await ResolveAuthorizationFromOrderAsync(node!, requestId, ct);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(PayPalMoney amount, string vaultId, string requestId, CancellationToken ct = default)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[] { new { amount = new { currency_code = amount.CurrencyCode, value = amount.Value } } },
            payment_source = new { card = new { vault_id = vaultId } }
        };
        var node = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, "authorize payment with saved card", ct, requestId);
        return await ResolveAuthorizationFromOrderAsync(node!, requestId, ct);
    }

    /// <summary>Reads the authorization out of a created PayPal order, calling the explicit
    /// authorize endpoint if the create call did not already produce one.</summary>
    private async Task<PayPalAuthorizationResult> ResolveAuthorizationFromOrderAsync(JsonNode orderNode, string requestId, CancellationToken ct)
    {
        DetectChallenge(orderNode);

        var orderId = orderNode["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal order response did not contain an id.", 502);

        var auth = FindFirstAuthorization(orderNode);
        if (auth is null)
        {
            // Order created but not yet authorized — authorize explicitly.
            var authNode = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{orderId}/authorize", new { },
                "authorize order", ct, requestId + "-auth");
            DetectChallenge(authNode!);
            auth = FindFirstAuthorization(authNode!);
        }

        if (auth is null)
        {
            throw new PayPalApiException($"PayPal did not return an authorization for order {orderId}.", 502);
        }

        var authId = auth["id"]!.GetValue<string>();
        var status = auth["status"]?.GetValue<string>() ?? "UNKNOWN";
        var expiresAt = ParseDate(auth["expiration_time"]);
        return new PayPalAuthorizationResult(orderId, authId, status, expiresAt);
    }

    private static JsonNode? FindFirstAuthorization(JsonNode orderNode)
    {
        var pus = orderNode["purchase_units"] as JsonArray;
        var auths = pus?.FirstOrDefault()?["payments"]?["authorizations"] as JsonArray;
        return auths?.FirstOrDefault();
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, PayPalMoney amount, string requestId, CancellationToken ct = default)
    {
        var body = new { amount = new { currency_code = amount.CurrencyCode, value = amount.Value } };
        var node = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body,
            "reauthorize payment", ct, requestId);
        var id = node?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal reauthorization response did not contain an id.", 502);
        var status = node!["status"]?.GetValue<string>() ?? "UNKNOWN";
        var expiresAt = ParseDate(node["expiration_time"]);
        return new PayPalAuthorizationResult(null, id, status, expiresAt);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null,
            "read authorization", ct, preferRepresentation: false);
        var id = node?["id"]?.GetValue<string>() ?? authorizationId;
        var status = node?["status"]?.GetValue<string>() ?? "UNKNOWN";
        var expiresAt = ParseDate(node?["expiration_time"]);
        return new PayPalAuthorizationResult(null, id, status, expiresAt);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null,
            "void authorization", ct, preferRepresentation: false);
    }

    // ---------------------------------------------------------------------
    // Capture
    // ---------------------------------------------------------------------
    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, PayPalMoney amount, string requestId, CancellationToken ct = default)
    {
        var body = new
        {
            amount = new { currency_code = amount.CurrencyCode, value = amount.Value },
            final_capture = true
        };
        var node = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body,
            "capture payment", ct, requestId);

        var captureId = node?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal capture response did not contain an id.", 502);
        var status = node!["status"]?.GetValue<string>() ?? "UNKNOWN";

        var breakdown = node["seller_receivable_breakdown"];
        var gross = ParseMoney(breakdown?["gross_amount"]) ?? ParseMoney(node["amount"]) ?? 0m;
        var fee = ParseMoney(breakdown?["paypal_fee"]);
        var net = ParseMoney(breakdown?["net_amount"]);
        var currency = breakdown?["gross_amount"]?["currency_code"]?.GetValue<string>()
                       ?? node["amount"]?["currency_code"]?.GetValue<string>()
                       ?? amount.CurrencyCode;

        return new PayPalCaptureResult(captureId, status, gross, fee, net, currency);
    }

    // ---------------------------------------------------------------------
    // Refund
    // ---------------------------------------------------------------------
    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, PayPalMoney? amount, string requestId, CancellationToken ct = default)
    {
        object body = amount is null
            ? new { }
            : new { amount = new { currency_code = amount.CurrencyCode, value = amount.Value } };

        var node = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body,
            "refund capture", ct, requestId);

        var refundId = node?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal refund response did not contain an id.", 502);
        var status = node!["status"]?.GetValue<string>() ?? "UNKNOWN";
        var refunded = ParseMoney(node["amount"]) ?? (amount is not null ? decimal.Parse(amount.Value, CultureInfo.InvariantCulture) : 0m);
        return new PayPalRefundResult(refundId, status, refunded);
    }

    // ---------------------------------------------------------------------
    // Vault
    // ---------------------------------------------------------------------
    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerId, string requestId, CancellationToken ct = default)
    {
        var body = new
        {
            payment_source = new { card = BuildCardObject(card) },
            customer = new { id = customerId }
        };
        var node = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, "vault card", ct, requestId);
        DetectChallenge(node!);

        var vaultId = node?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal vault response did not contain a token id.", 502);
        var cardNode = node!["payment_source"]?["card"];
        var brand = cardNode?["brand"]?.GetValue<string>();
        var last4 = cardNode?["last_digits"]?.GetValue<string>();
        var expiry = cardNode?["expiry"]?.GetValue<string>();
        return new VaultedCardResult(vaultId, brand, last4, expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null,
                "delete vaulted card", ct, preferRepresentation: false);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal — treat as success so our own record can be removed.
            _logger.LogInformation("Vault token {VaultId} not found at PayPal on delete; treating as removed.", vaultId);
        }
    }

    // ---------------------------------------------------------------------
    // Transaction search / reconciliation
    // ---------------------------------------------------------------------
    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();

        // PayPal caps a single query at a 31-day window, so walk the range in chunks.
        var chunkStart = from;
        while (chunkStart < to)
        {
            var chunkEnd = chunkStart.AddDays(31);
            if (chunkEnd > to) chunkEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var query = HttpUtility.ParseQueryString(string.Empty);
                query["start_date"] = FormatReportingDate(chunkStart);
                query["end_date"] = FormatReportingDate(chunkEnd);
                query["fields"] = "transaction_info";
                query["page_size"] = "500";
                query["page"] = page.ToString(CultureInfo.InvariantCulture);

                var node = await SendAsync(HttpMethod.Get, $"/v1/reporting/transactions?{query}", null,
                    "search transactions", ct, preferRepresentation: false);

                var details = node?["transaction_details"] as JsonArray;
                if (details is not null)
                {
                    foreach (var d in details)
                    {
                        var info = d?["transaction_info"];
                        if (info is null) continue;
                        var id = info["transaction_id"]?.GetValue<string>();
                        if (string.IsNullOrEmpty(id)) continue;
                        results.Add(new PayPalTransaction(
                            id,
                            info["transaction_status"]?.GetValue<string>() ?? "UNKNOWN",
                            ParseMoney(info["transaction_amount"]) ?? 0m,
                            info["transaction_amount"]?["currency_code"]?.GetValue<string>() ?? Currency,
                            ParseDate(info["transaction_initiation_date"]) ?? chunkStart,
                            info["transaction_event_code"]?.GetValue<string>()));
                    }
                }

                totalPages = node?["total_pages"]?.GetValue<int>() ?? 1;
                page++;
            }
            while (page <= totalPages);

            chunkStart = chunkEnd;
        }

        return results;
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------
    private static object BuildCardObject(CardDetails card)
    {
        object? billing = null;
        if (card.AddressLine1 is not null || card.PostalCode is not null || card.CountryCode is not null)
        {
            billing = new
            {
                address_line_1 = card.AddressLine1,
                address_line_2 = card.AddressLine2,
                admin_area_2 = card.City,
                admin_area_1 = card.State,
                postal_code = card.PostalCode,
                country_code = card.CountryCode
            };
        }

        return new
        {
            number = card.Number,
            expiry = card.ExpiryYearMonth,
            security_code = card.SecurityCode,
            name = card.Name,
            billing_address = billing
        };
    }

    /// <summary>Throws if PayPal signalled a payer-approval contingency (3DS/SCA), which
    /// this server-to-server integration deliberately does not attempt to complete.</summary>
    private static void DetectChallenge(JsonNode node)
    {
        var status = node["status"]?.GetValue<string>();
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalChallengeException(
                "PayPal requires the shopper to approve this payment in a browser (PAYER_ACTION_REQUIRED / 3-D Secure). " +
                "This integration is server-to-server only and does not perform browser approval.");
        }

        var links = node["links"] as JsonArray;
        if (links is not null && links.Any(l => string.Equals(l?["rel"]?.GetValue<string>(), "payer-action", StringComparison.OrdinalIgnoreCase)))
        {
            throw new PayPalChallengeException(
                "PayPal returned a payer-action approval link (3-D Secure / SCA challenge). " +
                "This integration is server-to-server only and does not perform browser approval.");
        }
    }

    private static decimal? ParseMoney(JsonNode? moneyNode)
    {
        var value = moneyNode?["value"]?.GetValue<string>();
        if (string.IsNullOrEmpty(value)) return null;
        return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseDate(JsonNode? node)
    {
        var s = node?.GetValue<string>();
        if (string.IsNullOrEmpty(s)) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
    }

    private static string FormatReportingDate(DateTimeOffset dt) =>
        dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "-0000";
}
