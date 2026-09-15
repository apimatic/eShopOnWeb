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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal REST implementation of <see cref="IPayPalGateway"/>. Owns OAuth token
/// acquisition/caching, idempotency + Prefer headers, error translation, and the
/// reconciliation date-range chunking/paging. Raw card details are forwarded to PayPal and
/// never logged.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private const string TokenCacheKey = "paypal:access_token";
    // PayPal's Transaction Search caps a single query window at ~31 days.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<PayPalGateway> _logger;
    private static readonly SemaphoreSlim TokenLock = new(1, 1);

    public PayPalGateway(
        HttpClient http, PayPalSettings settings, IMemoryCache cache, IAppLogger<PayPalGateway> logger)
    {
        _http = http;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Orders / auth / capture

    public async Task<PayPalOrderResult> CreateAuthorizationOrderAsync(
        decimal amount, string currencyCode, string invoiceId, string customId, string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray(new JsonObject
            {
                ["invoice_id"] = invoiceId,
                ["custom_id"] = customId,
                ["amount"] = Money(amount, currencyCode)
            })
        };

        var json = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, "return=minimal", cancellationToken);
        return new PayPalOrderResult(
            json?["id"]?.GetValue<string>() ?? throw Unexpected("create order returned no id"),
            json?["status"]?.GetValue<string>() ?? "UNKNOWN");
    }

    public Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(
        string payPalOrderId, CardPaymentDetails card, string requestId, CancellationToken cancellationToken = default)
        => AuthorizeOrderAsync(payPalOrderId, new JsonObject { ["card"] = BuildCard(card) }, requestId, cancellationToken);

    public Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultAsync(
        string payPalOrderId, string vaultId, string requestId, CancellationToken cancellationToken = default)
        => AuthorizeOrderAsync(payPalOrderId,
            new JsonObject { ["card"] = new JsonObject { ["vault_id"] = vaultId } }, requestId, cancellationToken);

    private async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(
        string payPalOrderId, JsonObject paymentSource, string requestId, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["payment_source"] = paymentSource };
        var json = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
            body, requestId, "return=representation", cancellationToken);

        var status = json?["status"]?.GetValue<string>() ?? "UNKNOWN";
        GuardAgainstChallenge(json, status);

        var authorization = json?["purchase_units"]?.AsArray()
            .SelectMany(pu => pu?["payments"]?["authorizations"]?.AsArray() ?? new JsonArray())
            .FirstOrDefault();

        if (authorization is null)
        {
            throw new PaymentDeclinedException(
                $"PayPal did not return an authorization for order {payPalOrderId} (status {status}).");
        }

        var authStatus = authorization["status"]?.GetValue<string>() ?? "UNKNOWN";
        if (authStatus.Equals("DENIED", StringComparison.OrdinalIgnoreCase)
            || authStatus.Equals("VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentDeclinedException(
                $"The card authorization for order {payPalOrderId} was {authStatus}.");
        }

        return new PayPalAuthorizationResult(
            payPalOrderId,
            authorization["id"]?.GetValue<string>() ?? throw Unexpected("authorization returned no id"),
            authStatus,
            ParseDate(authorization["expiration_time"]));
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default)
    {
        var json = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}",
            null, null, null, cancellationToken);
        var orderId = json?["supplementary_data"]?["related_ids"]?["order_id"]?.GetValue<string>() ?? string.Empty;
        return new PayPalAuthorizationResult(
            orderId,
            json?["id"]?.GetValue<string>() ?? authorizationId,
            json?["status"]?.GetValue<string>() ?? "UNKNOWN",
            ParseDate(json?["expiration_time"]));
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currencyCode, string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["amount"] = Money(amount, currencyCode) };
        var json = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body, requestId, "return=representation", cancellationToken);
        return new PayPalAuthorizationResult(
            string.Empty,
            json?["id"]?.GetValue<string>() ?? authorizationId,
            json?["status"]?.GetValue<string>() ?? "UNKNOWN",
            ParseDate(json?["expiration_time"]));
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string invoiceId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["invoice_id"] = invoiceId,
            ["final_capture"] = true
        };
        var json = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, requestId, "return=representation", cancellationToken);

        var breakdown = json?["seller_receivable_breakdown"];
        var gross = ParseMoney(breakdown?["gross_amount"]);
        var fee = ParseMoney(breakdown?["paypal_fee"]);
        var net = ParseMoney(breakdown?["net_amount"]);
        var currency = breakdown?["gross_amount"]?["currency_code"]?.GetValue<string>()
            ?? json?["amount"]?["currency_code"]?.GetValue<string>()
            ?? _settings.Currency;

        return new PayPalCaptureResult(
            json?["id"]?.GetValue<string>() ?? throw Unexpected("capture returned no id"),
            json?["status"]?.GetValue<string>() ?? "UNKNOWN",
            gross, fee, net, currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            null, null, "return=minimal", cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currencyCode, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        JsonObject? body = amount is decimal value
            ? new JsonObject { ["amount"] = Money(value, currencyCode) }
            : new JsonObject();

        var json = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            body, idempotencyKey, "return=representation", cancellationToken);

        return new PayPalRefundResult(
            json?["id"]?.GetValue<string>() ?? throw Unexpected("refund returned no id"),
            json?["status"]?.GetValue<string>() ?? "UNKNOWN",
            ParseMoney(json?["amount"]),
            json?["amount"]?["currency_code"]?.GetValue<string>() ?? currencyCode);
    }

    // ------------------------------------------------------------------------------- Vault

    public async Task<VaultedCard> VaultCardAsync(
        CardPaymentDetails card, string requestId, CancellationToken cancellationToken = default)
    {
        // 1) Setup token from the raw card.
        var setupBody = new JsonObject { ["payment_source"] = new JsonObject { ["card"] = BuildCard(card) } };
        var setup = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens",
            setupBody, $"{requestId}-setup", "return=representation", cancellationToken);

        var setupStatus = setup?["status"]?.GetValue<string>() ?? "UNKNOWN";
        GuardAgainstChallenge(setup, setupStatus);
        var setupTokenId = setup?["id"]?.GetValue<string>() ?? throw Unexpected("setup token returned no id");

        // 2) Exchange for a durable payment token.
        var tokenBody = new JsonObject
        {
            ["payment_source"] = new JsonObject
            {
                ["token"] = new JsonObject { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
            }
        };
        var token = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            tokenBody, $"{requestId}-token", "return=representation", cancellationToken);

        var vaultId = token?["id"]?.GetValue<string>() ?? throw Unexpected("payment token returned no id");
        var cardNode = token?["payment_source"]?["card"];
        return new VaultedCard(
            vaultId,
            cardNode?["brand"]?.GetValue<string>() ?? "Card",
            cardNode?["last_digits"]?.GetValue<string>() ?? "0000",
            cardNode?["expiry"]?.GetValue<string>() ?? card.Expiry,
            cardNode?["name"]?.GetValue<string>() ?? card.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}",
            null, null, null, cancellationToken);
    }

    // ----------------------------------------------------------------------- Reconciliation

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // Chunk the whole range into PayPal's maximum window and page each chunk fully, so
        // the report covers the entire range rather than just its first window/page.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var query = $"/v1/reporting/transactions?start_date={Rfc3339(windowStart)}&end_date={Rfc3339(windowEnd)}"
                    + $"&fields=transaction_info&page_size=100&page={page}&balance_affecting_records_only=N";
                var json = await SendAsync(HttpMethod.Get, query, null, null, null, cancellationToken);

                foreach (var detail in json?["transaction_details"]?.AsArray() ?? new JsonArray())
                {
                    var info = detail?["transaction_info"];
                    if (info is null) continue;
                    results.Add(new PayPalTransaction(
                        info["transaction_id"]?.GetValue<string>() ?? string.Empty,
                        info["invoice_id"]?.GetValue<string>(),
                        info["custom_field"]?.GetValue<string>(),
                        ParseMoney(info["transaction_amount"]),
                        ParseMoney(info["fee_amount"]),
                        info["transaction_amount"]?["currency_code"]?.GetValue<string>() ?? _settings.Currency,
                        info["transaction_status"]?.GetValue<string>() ?? string.Empty,
                        info["transaction_event_code"]?.GetValue<string>() ?? string.Empty,
                        ParseDate(info["transaction_initiation_date"]) ?? windowStart));
                }

                totalPages = json?["total_pages"]?.GetValue<int>() ?? 1;
                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd == to ? to : windowEnd;
            if (windowEnd == to) break;
        }

        return results;
    }

    // --------------------------------------------------------------------------- transport

    private async Task<JsonNode?> SendAsync(
        HttpMethod method, string path, JsonNode? body, string? requestId, string? prefer,
        CancellationToken cancellationToken)
    {
        var response = await SendOnceAsync(method, path, body, requestId, prefer, cancellationToken);

        // A stale access token surfaces as 401 — refresh once and retry.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _cache.Remove(TokenCacheKey);
            response.Dispose();
            response = await SendOnceAsync(method, path, body, requestId, prefer, cancellationToken);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException((int)response.StatusCode, payload);
        }

        return string.IsNullOrWhiteSpace(payload) ? null : JsonNode.Parse(payload);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method, string path, JsonNode? body, string? requestId, string? prefer,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (!string.IsNullOrEmpty(prefer))
        {
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        }
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        return await _http.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(TokenCacheKey, out string? again) && !string.IsNullOrEmpty(again))
            {
                return again!;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var basic = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _http.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException((int)response.StatusCode, payload);
            }

            var json = JsonNode.Parse(payload);
            var token = json?["access_token"]?.GetValue<string>()
                ?? throw Unexpected("token response had no access_token");
            var expiresIn = json?["expires_in"]?.GetValue<int>() ?? 3600;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            var ttl = TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60));
            _cache.Set(TokenCacheKey, token, ttl);
            return token;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    // ----------------------------------------------------------------------------- helpers

    private static JsonObject Money(decimal amount, string currencyCode) => new()
    {
        ["currency_code"] = currencyCode,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static JsonObject BuildCard(CardPaymentDetails card)
    {
        var node = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode
        };
        if (!string.IsNullOrWhiteSpace(card.Name)) node["name"] = card.Name;
        if (card.BillingAddress is CardBillingAddress addr)
        {
            var a = new JsonObject { ["country_code"] = addr.CountryCode };
            if (!string.IsNullOrWhiteSpace(addr.AddressLine1)) a["address_line_1"] = addr.AddressLine1;
            if (!string.IsNullOrWhiteSpace(addr.AddressLine2)) a["address_line_2"] = addr.AddressLine2;
            if (!string.IsNullOrWhiteSpace(addr.AdminArea1)) a["admin_area_1"] = addr.AdminArea1;
            if (!string.IsNullOrWhiteSpace(addr.AdminArea2)) a["admin_area_2"] = addr.AdminArea2;
            if (!string.IsNullOrWhiteSpace(addr.PostalCode)) a["postal_code"] = addr.PostalCode;
            node["billing_address"] = a;
        }
        return node;
    }

    private static void GuardAgainstChallenge(JsonNode? json, string status)
    {
        if (status.Equals("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This integration is browser-less and cannot complete such a payment.");
        }

        var hasPayerAction = json?["links"]?.AsArray()
            .Any(l => (l?["rel"]?.GetValue<string>() ?? string.Empty)
                .Contains("payer-action", StringComparison.OrdinalIgnoreCase)) ?? false;
        if (hasPayerAction)
        {
            throw new PaymentChallengeRequiredException(
                "PayPal returned a payer-action (browser approval) requirement for this card. " +
                "This integration is browser-less and cannot complete such a payment.");
        }
    }

    private static decimal ParseMoney(JsonNode? money)
    {
        var value = money?["value"]?.GetValue<string>();
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private static DateTimeOffset? ParseDate(JsonNode? node)
    {
        var value = node?.GetValue<string>();
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto : null;
    }

    private static string Rfc3339(DateTimeOffset value)
        => Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    private PayPalApiException BuildApiException(int statusCode, string payload)
    {
        string? name = null, issue = null, debugId = null, message = null;
        try
        {
            var json = JsonNode.Parse(payload);
            name = json?["name"]?.GetValue<string>();
            message = json?["message"]?.GetValue<string>();
            debugId = json?["debug_id"]?.GetValue<string>();
            issue = json?["details"]?.AsArray().FirstOrDefault()?["issue"]?.GetValue<string>();
        }
        catch
        {
            // Non-JSON error body — fall through with the raw payload as the message.
            message = payload;
        }

        var summary = $"PayPal API error {statusCode}: {name ?? "error"}{(issue is null ? "" : $"/{issue}")}" +
                      $" - {message}. debug_id={debugId ?? "n/a"}";
        _logger.LogWarning(summary);
        return new PayPalApiException(statusCode, name, issue, debugId, summary);
    }

    private static InvalidOperationException Unexpected(string what)
        => new($"Unexpected PayPal response: {what}.");
}
