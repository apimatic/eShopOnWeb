using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The PayPal REST API implementation of <see cref="IPayPalClient"/>. Owns HTTP, OAuth token
/// caching, JSON, idempotency headers and error translation. Nothing else in the app talks to
/// PayPal directly.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private const string TokenCacheKey = "PayPal.AccessToken";
    private static readonly SemaphoreSlim TokenLock = new(1, 1);

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings,
        IMemoryCache cache, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolveBaseUrl();

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- Orders

    public async Task<CreateOrderResult> CreateAuthorizationOrderAsync(
        decimal amount, string currency, string merchantReference, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["invoice_id"] = merchantReference,
                    ["custom_id"] = merchantReference,
                    ["amount"] = new JsonObject
                    {
                        ["currency_code"] = currency,
                        ["value"] = Money(amount)
                    }
                }
            }
        };

        var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            requestId: requestId, prefer: "return=minimal", cancellationToken: cancellationToken);

        var id = response?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal did not return an order id.", 502, null, null);
        var status = response?["status"]?.GetValue<string>() ?? "UNKNOWN";
        return new CreateOrderResult(id, status);
    }

    public async Task<AuthorizationResult> AuthorizeOrderAsync(
        string payPalOrderId, PaymentInstrument instrument, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["payment_source"] = new JsonObject { ["card"] = BuildCardNode(instrument) } };

        var response = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", body,
            requestId: requestId, prefer: "return=representation", cancellationToken: cancellationToken);

        return ParseAuthorizationFromOrder(response, payPalOrderId);
    }

    // ---------------------------------------------------------------- Payments (authorizations)

    public async Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null,
            cancellationToken: cancellationToken);
        return ParseAuthorization(response);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject { ["currency_code"] = currency, ["value"] = Money(amount) }
        };

        var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body,
            requestId: requestId, prefer: "return=representation", cancellationToken: cancellationToken);
        return ParseAuthorization(response);
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string requestId, bool finalCapture, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject { ["currency_code"] = currency, ["value"] = Money(amount) },
            ["final_capture"] = finalCapture
        };

        var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body,
            requestId: requestId, prefer: "return=representation", cancellationToken: cancellationToken);

        var id = response?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal did not return a capture id.", 502, null, null);
        var status = response?["status"]?.GetValue<string>() ?? "UNKNOWN";

        var breakdown = response?["seller_receivable_breakdown"];
        var gross = ParseMoney(breakdown?["gross_amount"]) ?? amount;
        var fee = ParseMoney(breakdown?["paypal_fee"]);
        var net = ParseMoney(breakdown?["net_amount"]);
        var currencyCode = breakdown?["gross_amount"]?["currency_code"]?.GetValue<string>() ?? currency;

        return new CaptureResult(id, status, gross, fee, net, currencyCode);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null,
            requestId: requestId, cancellationToken: cancellationToken);
    }

    public async Task<RefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        JsonObject? body = null;
        if (amount.HasValue)
        {
            body = new JsonObject
            {
                ["amount"] = new JsonObject { ["currency_code"] = currency, ["value"] = Money(amount.Value) }
            };
        }

        var response = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body,
            requestId: requestId, prefer: "return=representation", cancellationToken: cancellationToken);

        var id = response?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal did not return a refund id.", 502, null, null);
        var status = response?["status"]?.GetValue<string>() ?? "UNKNOWN";
        var gross = ParseMoney(response?["amount"]) ?? amount ?? 0m;
        var totalRefunded = ParseMoney(response?["seller_payable_breakdown"]?["total_refunded_amount"]);
        return new RefundResult(id, status, gross, totalRefunded);
    }

    // ---------------------------------------------------------------- Vault

    public async Task<VaultCardResult> VaultCardAsync(
        CardDetails card, string? customerId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = BuildRawCardNode(card) }
        };
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            body["customer"] = new JsonObject { ["id"] = customerId };
        }

        var response = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body,
            requestId: requestId, cancellationToken: cancellationToken);

        var tokenId = response?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal did not return a vault token id.", 502, null, null);
        var returnedCustomerId = response?["customer"]?["id"]?.GetValue<string>() ?? customerId ?? string.Empty;
        var cardNode = response?["payment_source"]?["card"];
        var brand = cardNode?["brand"]?.GetValue<string>();
        var last4 = cardNode?["last_digits"]?.GetValue<string>();
        var expiry = cardNode?["expiry"]?.GetValue<string>();

        return new VaultCardResult(tokenId, returnedCustomerId, brand, last4, expiry);
    }

    public async Task DeleteVaultTokenAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null,
            cancellationToken: cancellationToken);
    }

    // ---------------------------------------------------------------- Transaction search

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // Transaction Search allows at most a 31-day window per request, so walk the range in chunks.
        var windowStart = from.ToUniversalTime();
        var end = to.ToUniversalTime();
        var maxWindow = TimeSpan.FromDays(31);

        while (windowStart < end)
        {
            var windowEnd = windowStart + maxWindow;
            if (windowEnd > end)
            {
                windowEnd = end;
            }

            await ReadAllPagesAsync(windowStart, windowEnd, results, cancellationToken);

            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ReadAllPagesAsync(
        DateTimeOffset start, DateTimeOffset end, List<PayPalTransaction> sink, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var page = 1;
        int totalPages;

        do
        {
            var startStr = Uri.EscapeDataString(start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            var endStr = Uri.EscapeDataString(end.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            var path = $"/v1/reporting/transactions?start_date={startStr}&end_date={endStr}" +
                       $"&fields=all&page_size={pageSize}&page={page}&total_required=true";

            var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken: cancellationToken);

            var details = response?["transaction_details"]?.AsArray();
            if (details is not null)
            {
                foreach (var detail in details)
                {
                    var info = detail?["transaction_info"];
                    if (info is null)
                    {
                        continue;
                    }

                    sink.Add(new PayPalTransaction(
                        TransactionId: info["transaction_id"]?.GetValue<string>(),
                        ReferenceId: info["paypal_reference_id"]?.GetValue<string>(),
                        EventCode: info["transaction_event_code"]?.GetValue<string>(),
                        Status: info["transaction_status"]?.GetValue<string>(),
                        Amount: ParseMoney(info["transaction_amount"]),
                        Currency: info["transaction_amount"]?["currency_code"]?.GetValue<string>(),
                        FeeAmount: ParseMoney(info["fee_amount"]),
                        InvoiceId: info["invoice_id"]?.GetValue<string>(),
                        CustomField: info["custom_field"]?.GetValue<string>(),
                        InitiationDate: ParseDate(info["transaction_initiation_date"])));
                }
            }

            totalPages = response?["total_pages"]?.GetValue<int>() ?? 1;
            page++;
        }
        while (page <= totalPages);
    }

    // ---------------------------------------------------------------- JSON helpers

    private static JsonObject BuildCardNode(PaymentInstrument instrument)
    {
        if (!string.IsNullOrWhiteSpace(instrument.VaultId))
        {
            return new JsonObject { ["vault_id"] = instrument.VaultId };
        }
        if (instrument.Card is not null)
        {
            return BuildRawCardNode(instrument.Card);
        }
        throw new PaymentException("No payment instrument was supplied (card details or a saved card).");
    }

    private static JsonObject BuildRawCardNode(CardDetails card)
    {
        var node = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };
        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            node["security_code"] = card.SecurityCode;
        }
        if (!string.IsNullOrWhiteSpace(card.CardholderName))
        {
            node["name"] = card.CardholderName;
        }
        if (card.BillingAddress is not null)
        {
            var addr = new JsonObject { ["country_code"] = card.BillingAddress.CountryCode };
            if (!string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1)) addr["address_line_1"] = card.BillingAddress.AddressLine1;
            if (!string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine2)) addr["address_line_2"] = card.BillingAddress.AddressLine2;
            if (!string.IsNullOrWhiteSpace(card.BillingAddress.AdminArea2)) addr["admin_area_2"] = card.BillingAddress.AdminArea2;
            if (!string.IsNullOrWhiteSpace(card.BillingAddress.AdminArea1)) addr["admin_area_1"] = card.BillingAddress.AdminArea1;
            if (!string.IsNullOrWhiteSpace(card.BillingAddress.PostalCode)) addr["postal_code"] = card.BillingAddress.PostalCode;
            node["billing_address"] = addr;
        }
        return node;
    }

    private static AuthorizationResult ParseAuthorizationFromOrder(JsonNode? order, string fallbackOrderId)
    {
        var authorizations = order?["purchase_units"]?.AsArray();
        JsonNode? authorization = null;
        if (authorizations is not null)
        {
            foreach (var unit in authorizations)
            {
                var auths = unit?["payments"]?["authorizations"]?.AsArray();
                if (auths is { Count: > 0 })
                {
                    authorization = auths[0];
                    break;
                }
            }
        }

        if (authorization is null)
        {
            throw new PayPalApiException(
                $"PayPal accepted order {fallbackOrderId} but returned no authorization. The payment may require buyer approval in a browser.",
                502, order?["status"]?.GetValue<string>(), null);
        }

        var cardNode = order?["payment_source"]?["card"];
        var result = ParseAuthorization(authorization);
        return result with
        {
            CardBrand = result.CardBrand ?? cardNode?["brand"]?.GetValue<string>(),
            CardLast4 = result.CardLast4 ?? cardNode?["last_digits"]?.GetValue<string>()
        };
    }

    private static AuthorizationResult ParseAuthorization(JsonNode? authorization)
    {
        var id = authorization?["id"]?.GetValue<string>()
            ?? throw new PayPalApiException("PayPal did not return an authorization id.", 502, null, null);
        var status = authorization?["status"]?.GetValue<string>() ?? "UNKNOWN";
        var expiresAt = ParseDate(authorization?["expiration_time"]);
        return new AuthorizationResult(id, status, expiresAt, null, null);
    }

    private static decimal? ParseMoney(JsonNode? moneyNode)
    {
        var value = moneyNode?["value"]?.GetValue<string>();
        if (value is null)
        {
            return null;
        }
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? ParseDate(JsonNode? dateNode)
    {
        var value = dateNode?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    // ---------------------------------------------------------------- HTTP

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body,
        string? requestId = null, string? prefer = null, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (!string.IsNullOrWhiteSpace(prefer))
        {
            request.Headers.TryAddWithoutValidation("Prefer", prefer);
        }
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            ThrowApiError(response.StatusCode, payload, method, path);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void ThrowApiError(HttpStatusCode statusCode, string payload, HttpMethod method, string path)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;
        try
        {
            var error = JsonNode.Parse(payload);
            name = error?["name"]?.GetValue<string>();
            message = error?["message"]?.GetValue<string>();
            debugId = error?["debug_id"]?.GetValue<string>();

            var details = error?["details"]?.AsArray();
            if (details is { Count: > 0 })
            {
                var issues = new List<string>();
                foreach (var detail in details)
                {
                    var issue = detail?["issue"]?.GetValue<string>();
                    var description = detail?["description"]?.GetValue<string>();
                    if (issue is not null || description is not null)
                    {
                        issues.Add($"{issue}: {description}".Trim(':', ' '));
                    }
                }
                if (issues.Count > 0)
                {
                    message = $"{message} ({string.Join("; ", issues)})";
                }
            }
        }
        catch (JsonException)
        {
            message = payload;
        }

        _logger.LogError("PayPal {Method} {Path} failed: {Status} {Name} debug_id={DebugId} message={Message}",
            method, path, (int)statusCode, name, debugId, message);

        throw new PayPalApiException(
            message ?? $"PayPal request failed with status {(int)statusCode}.",
            (int)statusCode, name, debugId);
    }

    // ---------------------------------------------------------------- OAuth token

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(TokenCacheKey, out cached) && !string.IsNullOrEmpty(cached))
            {
                return cached;
            }

            _settings.Validate();

            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                ThrowApiError(response.StatusCode, payload, HttpMethod.Post, "/v1/oauth2/token");
            }

            var json = JsonNode.Parse(payload);
            var token = json?["access_token"]?.GetValue<string>()
                ?? throw new PayPalApiException("PayPal did not return an access token.", 502, null, null);
            var expiresIn = json?["expires_in"]?.GetValue<int>() ?? 3600;

            // Refresh a minute early to avoid using a token that expires mid-flight.
            var lifetime = TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60));
            _cache.Set(TokenCacheKey, token, lifetime);
            return token;
        }
        finally
        {
            TokenLock.Release();
        }
    }
}
