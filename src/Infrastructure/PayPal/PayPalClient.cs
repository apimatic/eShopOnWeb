using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Plain-HTTP client for the PayPal REST APIs used by this integration.
/// Holds no card data beyond the lifetime of a single call and never logs payloads.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private const int MaxTransactionDaysPerRequest = 31;

    private readonly PayPalOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PayPalClient> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public string Currency => _options.Currency;

    public PayPalClient(IOptions<PayPalOptions> options, IHttpClientFactory httpClientFactory,
        ILogger<PayPalClient> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(e.g. from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables).");
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("PayPal");
        client.BaseAddress = new Uri(_options.ResolveBaseUrl() + "/");
        return client;
    }

    private async Task<string> GetAccessTokenAsync()
    {
        await _tokenLock.WaitAsync();
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            _logger.LogInformation("Requesting PayPal OAuth access token from {BaseUrl}", _options.ResolveBaseUrl());

            var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes(
                    $"{_options.ClientId}:{_options.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new PayPalApiException(
                    $"PayPal token request failed with HTTP {(int)response.StatusCode}.", (int)response.StatusCode);
            }

            using var doc = JsonDocument.Parse(body);
            _accessToken = doc.RootElement.GetProperty("access_token").GetString()
                ?? throw new PayPalApiException("PayPal token response contained no access_token.");
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 300));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body = null,
        Guid? requestId = null, bool preferRepresentation = true)
    {
        var token = await GetAccessTokenAsync();
        var client = CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (requestId.HasValue)
        {
            request.Headers.Add("PayPal-Request-Id", requestId.Value.ToString("D"));
        }
        if (preferRepresentation && method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        // Request/response bodies are deliberately never logged: payment requests carry card details.
        _logger.LogInformation("PayPal call: {Method} {Path}", method.Method, path);
        var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode || (int)response.StatusCode == 204)
        {
            return string.IsNullOrWhiteSpace(responseBody)
                ? JsonDocument.Parse("{}")
                : JsonDocument.Parse(responseBody);
        }

        throw await BuildApiExceptionAsync((int)response.StatusCode, responseBody);
    }

    private static async Task<PayPalApiException> BuildApiExceptionAsync(int statusCode, string responseBody)
    {
        string message = $"PayPal request failed with HTTP {statusCode}.";
        string? issue = null;
        string? debugId = null;
        try
        {
            await using var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(responseBody));
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msg)) message = $"PayPal: {msg.GetString()}";
            if (root.TryGetProperty("debug_id", out var dbg)) debugId = dbg.GetString();
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                details.GetArrayLength() > 0)
            {
                var first = details[0];
                if (first.TryGetProperty("issue", out var iss)) issue = iss.GetString();
                if (first.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String)
                {
                    message = $"PayPal: {desc.GetString()}";
                }
            }
        }
        catch (JsonException)
        {
            // keep defaults
        }
        return new PayPalApiException(message, statusCode, issue, debugId);
    }

    private static PayPalMoney Money(JsonElement element)
    {
        var currency = element.GetProperty("currency_code").GetString() ?? "";
        var value = decimal.Parse(element.GetProperty("value").GetString() ?? "0",
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return new PayPalMoney(currency, value);
    }

    private static PayPalMoney? MoneyOrNull(JsonElement parent, string name)
    {
        var el = ElementOrNull(parent, name);
        return el == null ? null : Money(el.Value);
    }

    private static JsonElement? ElementOrNull(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var el) &&
           el.ValueKind == JsonValueKind.Object ? el : null;

    private static PayPalAuthorizationInfo ParseAuthorization(JsonElement element)
    {
        DateTimeOffset? expiration = null;
        if (element.TryGetProperty("expiration_time", out var expEl) && expEl.ValueKind == JsonValueKind.String)
        {
            expiration = expEl.GetDateTimeOffset();
        }
        return new PayPalAuthorizationInfo(
            element.GetProperty("id").GetString() ?? "",
            element.GetProperty("status").GetString() ?? "",
            Money(element.GetProperty("amount")),
            expiration);
    }

    private static PayPalAuthorizationInfo? FindAuthorization(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.TryGetProperty("purchase_units", out var units) && units.GetArrayLength() > 0)
        {
            var unit = units[0];
            if (unit.TryGetProperty("payments", out var payments) &&
                payments.TryGetProperty("authorizations", out var auths) && auths.GetArrayLength() > 0)
            {
                return ParseAuthorization(auths[0]);
            }
        }
        return null;
    }

    public async Task<(string OrderId, PayPalAuthorizationInfo Authorization)> AuthorizeAsync(
        decimal amount, string invoiceId, string customId, PayPalCardPayment? card, string? vaultId, Guid requestId)
    {
        var cardSource = new Dictionary<string, object>();
        if (vaultId != null)
        {
            cardSource["vault_id"] = vaultId;
        }
        else if (card != null)
        {
            cardSource["number"] = card.Number;
            cardSource["expiry"] = card.Expiry;
            cardSource["name"] = card.Name;
            cardSource["billing_address"] = new Dictionary<string, string>
            {
                ["address_line_1"] = card.BillingAddress.Line1,
                ["address_line_2"] = card.BillingAddress.Line2,
                ["admin_area_2"] = card.BillingAddress.City,
                ["admin_area_1"] = card.BillingAddress.State,
                ["postal_code"] = card.BillingAddress.PostalCode,
                ["country_code"] = card.BillingAddress.CountryCode
            };
        }

        var body = new Dictionary<string, object>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["amount"] = new Dictionary<string, string>
                    {
                        ["currency_code"] = _options.Currency,
                        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
                    },
                    ["invoice_id"] = invoiceId,
                    ["custom_id"] = customId
                }
            },
            ["payment_source"] = new Dictionary<string, object> { ["card"] = cardSource }
        };

        var doc = await SendAsync(HttpMethod.Post, "v2/checkout/orders", body, requestId);
        var orderId = doc.RootElement.GetProperty("id").GetString()
            ?? throw new PayPalApiException("PayPal order response contained no id.");

        var authorization = FindAuthorization(doc);
        if (authorization == null)
        {
            // With a card source PayPal authorizes inline; for other flows authorize explicitly.
            var authDoc = await SendAsync(HttpMethod.Post, $"v2/checkout/orders/{orderId}/authorize", null, requestId);
            authorization = FindAuthorization(authDoc)
                ?? throw new PayPalApiException(
                    $"PayPal authorization for order {orderId} did not produce an authorization.");
        }
        return (orderId, authorization);
    }

    public async Task<PayPalAuthorizationInfo> ReauthorizeAsync(string authorizationId, Guid requestId)
    {
        var doc = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize");
        return ParseAuthorization(doc.RootElement);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, Guid requestId)
    {
        await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", null, requestId);
    }

    public async Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId)
    {
        var doc = await SendAsync(HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}");
        return ParseAuthorization(doc.RootElement);
    }

    public async Task<PayPalCaptureInfo> CaptureAsync(string authorizationId, decimal amount, string invoiceId, Guid requestId)
    {
        var body = new Dictionary<string, object>
        {
            ["amount"] = new Dictionary<string, string>
            {
                ["currency_code"] = _options.Currency,
                ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
            },
            ["invoice_id"] = invoiceId,
            ["final_capture"] = true
        };
        var doc = await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture",
            body, requestId);
        var root = doc.RootElement;
        var breakdown = ElementOrNull(root, "seller_receivable_breakdown");
        return new PayPalCaptureInfo(
            root.GetProperty("id").GetString() ?? "",
            root.GetProperty("status").GetString() ?? "",
            Money(root.GetProperty("amount")),
            breakdown == null ? null : MoneyOrNull(breakdown.Value, "paypal_fee"),
            breakdown == null ? null : MoneyOrNull(breakdown.Value, "net_amount"));
    }

    public async Task<PayPalRefundInfo> RefundAsync(string captureId, decimal? amount, string invoiceId, Guid requestId)
    {
        var body = new Dictionary<string, object>
        {
            ["invoice_id"] = invoiceId
        };
        if (amount.HasValue)
        {
            body["amount"] = new Dictionary<string, string>
            {
                ["currency_code"] = _options.Currency,
                ["value"] = amount.Value.ToString("0.00", CultureInfo.InvariantCulture)
            };
        }
        var doc = await SendAsync(HttpMethod.Post, $"v2/payments/captures/{captureId}/refund", body, requestId);
        var root = doc.RootElement;
        return new PayPalRefundInfo(
            root.GetProperty("id").GetString() ?? "",
            root.GetProperty("status").GetString() ?? "",
            Money(root.GetProperty("amount")));
    }

    public async Task<PayPalPaymentTokenInfo> CreatePaymentTokenAsync(PayPalCardPayment card, string customerId, Guid requestId)
    {
        var body = new Dictionary<string, object>
        {
            ["payment_source"] = new Dictionary<string, object>
            {
                ["card"] = new Dictionary<string, object>
                {
                    ["number"] = card.Number,
                    ["expiry"] = card.Expiry,
                    ["name"] = card.Name,
                    ["billing_address"] = new Dictionary<string, string>
                    {
                        ["address_line_1"] = card.BillingAddress.Line1,
                        ["address_line_2"] = card.BillingAddress.Line2,
                        ["admin_area_2"] = card.BillingAddress.City,
                        ["admin_area_1"] = card.BillingAddress.State,
                        ["postal_code"] = card.BillingAddress.PostalCode,
                        ["country_code"] = card.BillingAddress.CountryCode
                    }
                }
            },
            ["customer"] = new Dictionary<string, object> { ["id"] = customerId }
        };

        var doc = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", body, requestId);
        var root = doc.RootElement;
        var cardEl = root.GetProperty("payment_source").GetProperty("card");
        return new PayPalPaymentTokenInfo(
            root.GetProperty("id").GetString() ?? "",
            root.TryGetProperty("customer", out var customer) &&
                customer.TryGetProperty("id", out var cid) ? cid.GetString() : null,
            cardEl.TryGetProperty("brand", out var brand) ? brand.GetString() : null,
            cardEl.TryGetProperty("last_digits", out var last4) ? last4.GetString() : null,
            cardEl.TryGetProperty("expiry", out var expiry) ? expiry.GetString() : null,
            cardEl.TryGetProperty("name", out var name) ? name.GetString() : null);
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{paymentTokenId}", null, null, preferRepresentation: false);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            // Already gone from the vault - deletion is effectively done.
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionInfo>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var all = new List<PayPalTransactionInfo>();
        var chunkStart = from;
        while (chunkStart < to)
        {
            var chunkEnd = chunkStart.AddDays(MaxTransactionDaysPerRequest);
            if (chunkEnd > to) chunkEnd = to;
            await CollectPageAsync(chunkStart, chunkEnd, all);
            chunkStart = chunkEnd;
        }
        return all;
    }

    private async Task CollectPageAsync(DateTimeOffset from, DateTimeOffset to, List<PayPalTransactionInfo> all)
    {
        var page = 1;
        int totalPages;
        do
        {
            var query = "v1/reporting/transactions" +
                        $"?start_date={Uri.EscapeDataString(FormatTransactionDate(from))}" +
                        $"&end_date={Uri.EscapeDataString(FormatTransactionDate(to))}" +
                        "&fields=all&page_size=100" +
                        $"&page={page}";
            var doc = await SendAsync(HttpMethod.Get, query, preferRepresentation: false);
            var root = doc.RootElement;

            totalPages = root.TryGetProperty("total_pages", out var tp) ? tp.GetInt32() : 1;
            if (root.TryGetProperty("transaction_details", out var details) &&
                details.ValueKind == JsonValueKind.Array)
            {
                foreach (var tx in details.EnumerateArray())
                {
                    all.Add(ParseTransaction(tx));
                }
            }
            page++;
        } while (page <= totalPages);
    }

    private static string FormatTransactionDate(DateTimeOffset value)
    {
        // PayPal requires an ISO-8601 timestamp with an explicit UTC offset, e.g. 2026-09-04T09:39:21+00:00.
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    private static PayPalTransactionInfo ParseTransaction(JsonElement tx)
    {
        var info = tx.GetProperty("transaction_info");
        var amount = info.TryGetProperty("transaction_amount", out var amt) &&
                     amt.ValueKind == JsonValueKind.Object
            ? new PayPalMoney(amt.GetProperty("currency_code").GetString() ?? "",
                decimal.Parse(amt.GetProperty("value").GetString() ?? "0", CultureInfo.InvariantCulture))
            : null;
        PayPalMoney? fee = info.TryGetProperty("fee_amount", out var feeEl) &&
                           feeEl.ValueKind == JsonValueKind.Object
            ? new PayPalMoney(feeEl.GetProperty("currency_code").GetString() ?? "",
                decimal.Parse(feeEl.GetProperty("value").GetString() ?? "0", CultureInfo.InvariantCulture))
            : null;
        PayPalMoney? net = info.TryGetProperty("transaction_amount", out var netAmt) &&
                           info.TryGetProperty("fee_amount", out var feeAmt)
            ? new PayPalMoney(netAmt.GetProperty("currency_code").GetString() ?? "",
                decimal.Parse(netAmt.GetProperty("value").GetString() ?? "0", CultureInfo.InvariantCulture) -
                decimal.Parse(feeAmt.GetProperty("value").GetString() ?? "0", CultureInfo.InvariantCulture))
            : null;

        var date = info.TryGetProperty("transaction_initiation_date", out var dateEl) &&
                   dateEl.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(dateEl.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
            : DateTimeOffset.MinValue;

        return new PayPalTransactionInfo(
            info.GetProperty("transaction_id").GetString() ?? "",
            info.TryGetProperty("transaction_event_code", out var evt) ? evt.GetString() ?? "" : "",
            info.TryGetProperty("transaction_status", out var st) ? st.GetString() ?? "" : "",
            date,
            amount,
            fee,
            net,
            info.TryGetProperty("invoice_id", out var inv) && inv.ValueKind == JsonValueKind.String ? inv.GetString() : null,
            info.TryGetProperty("custom_field", out var custom) && custom.ValueKind == JsonValueKind.String ? custom.GetString() : null,
            info.TryGetProperty("transaction_reference_id", out var reference) && reference.ValueKind == JsonValueKind.String ? reference.GetString() : null);
    }
}
