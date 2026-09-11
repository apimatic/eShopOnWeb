using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// HTTP implementation of <see cref="IPayPalClient"/> against the PayPal REST API. Built directly
/// over verified endpoints (Orders v2, Payments v2, Vault v3, Reporting v1). Access tokens are
/// cached and refreshed; all money values are formatted to the currency's minor units.
/// </summary>
public class PayPalClient : IPayPalClient
{
    // ISO-4217 currencies with no minor units (subset PayPal supports).
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "HUF", "TWD"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(IHttpClientFactory httpClientFactory, PayPalSettings settings, ILogger<PayPalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolveBaseUrl();

    public const string HttpClientName = "paypal";

    // ---------------------------------------------------------------- Orders / authorize

    public async Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string invoiceId, string customId, CardDetails card, string requestId, CancellationToken cancellationToken = default)
    {
        var cardBody = BuildCardBody(card, includeSecurityCode: true);
        var body = BuildOrderBody(amount, invoiceId, customId, new Dictionary<string, object?> { ["card"] = cardBody });
        return await CreateAuthorizationOrderAsync(body, requestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(
        decimal amount, string invoiceId, string customId, string vaultId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = BuildOrderBody(amount, invoiceId, customId, new Dictionary<string, object?>
        {
            ["card"] = new Dictionary<string, object?> { ["vault_id"] = vaultId }
        });
        return await CreateAuthorizationOrderAsync(body, requestId, cancellationToken);
    }

    private object BuildOrderBody(decimal amount, string invoiceId, string customId, object paymentSource)
    {
        return new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["reference_id"] = "default",
                    ["invoice_id"] = invoiceId,
                    ["custom_id"] = customId,
                    ["amount"] = Money(amount)
                }
            },
            ["payment_source"] = paymentSource
        };
    }

    private async Task<PayPalAuthorizationResult> CreateAuthorizationOrderAsync(object body, string requestId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, requestId, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc.RootElement;

        var status = GetString(root, "status") ?? "UNKNOWN";
        var orderId = GetString(root, "id") ?? throw new PayPalApiException("PayPal did not return an order id.");

        // If PayPal wants the shopper to approve in a browser, we deliberately stop here.
        if (RequiresPayerAction(root, status))
        {
            throw new PayPalChallengeRequiredException(
                "PayPal returned a payer-action / authentication challenge for this card. " +
                "This integration does not perform a browser approval round-trip.");
        }

        var (brand, last4) = ReadCardSummary(root);

        var authId = FindAuthorizationId(root);
        if (authId is null)
        {
            throw new PayPalApiException(
                $"PayPal order {orderId} is '{status}' but returned no authorization. Raw: {Truncate(root.GetRawText())}");
        }

        return new PayPalAuthorizationResult(orderId, authId, status, brand, last4);
    }

    // ---------------------------------------------------------------- Capture / reauthorize / void

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Money(amount),
            ["final_capture"] = true
        };

        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, requestId, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc.RootElement;

        var captureId = GetString(root, "id") ?? throw new PayPalApiException("PayPal did not return a capture id.");
        var status = GetString(root, "status") ?? "UNKNOWN";

        decimal gross = amount, fee = 0m, net = amount;
        var currency = _settings.Currency;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadMoney(breakdown, "gross_amount") ?? gross;
            fee = ReadMoney(breakdown, "paypal_fee") ?? 0m;
            net = ReadMoney(breakdown, "net_amount") ?? (gross - fee);
            currency = ReadCurrency(breakdown, "gross_amount") ?? currency;
        }

        return new PayPalCaptureResult(captureId, status, gross, fee, net, currency);
    }

    public async Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        return GetString(doc.RootElement, "status") ?? "UNKNOWN";
    }

    public async Task<PayPalReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Money(amount) };

        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, Guid.NewGuid().ToString("N"), cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc.RootElement;

        var newId = GetString(root, "id") ?? throw new PayPalApiException("PayPal did not return a reauthorization id.");
        var status = GetString(root, "status") ?? "UNKNOWN";
        return new PayPalReauthorizeResult(newId, status);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, Guid.NewGuid().ToString("N"), cancellationToken);
        // 204 No Content on success; SendAsync already validated the status.
        response.Dispose();
    }

    // ---------------------------------------------------------------- Refund

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string invoiceId, string customId, string requestId, CancellationToken cancellationToken = default)
    {
        object? body = null;
        if (amount is decimal a)
        {
            body = new Dictionary<string, object?>
            {
                ["amount"] = Money(a),
                ["invoice_id"] = invoiceId,
                ["custom_id"] = customId
            };
        }

        using var response = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, requestId, cancellationToken);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc.RootElement;

        var refundId = GetString(root, "id") ?? throw new PayPalApiException("PayPal did not return a refund id.");
        var status = GetString(root, "status") ?? "UNKNOWN";

        decimal? totalRefunded = null;
        if (root.TryGetProperty("seller_payable_breakdown", out var breakdown))
        {
            totalRefunded = ReadMoney(breakdown, "total_refunded_amount");
        }

        return new PayPalRefundResult(refundId, status, totalRefunded);
    }

    // ---------------------------------------------------------------- Vault (save card)

    public async Task<PayPalVaultCardResult> VaultCardAsync(
        CardDetails card, string? existingCustomerId, string requestId, CancellationToken cancellationToken = default)
    {
        // Step 1: create a setup token from the raw card.
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCardBody(card, includeSecurityCode: true) }
        };
        if (!string.IsNullOrEmpty(existingCustomerId))
        {
            setupBody["customer"] = new Dictionary<string, object?> { ["id"] = existingCustomerId };
        }

        string setupTokenId;
        using (var setupResponse = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody, requestId, cancellationToken))
        using (var setupDoc = await ReadJsonAsync(setupResponse, cancellationToken))
        {
            setupTokenId = GetString(setupDoc.RootElement, "id")
                ?? throw new PayPalApiException("PayPal did not return a setup token id.");
        }

        // Step 2: upgrade the setup token to a permanent payment (vault) token.
        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?> { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
            }
        };

        using var tokenResponse = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody, Guid.NewGuid().ToString("N"), cancellationToken);
        using var tokenDoc = await ReadJsonAsync(tokenResponse, cancellationToken);
        var root = tokenDoc.RootElement;

        var vaultId = GetString(root, "id") ?? throw new PayPalApiException("PayPal did not return a payment token id.");
        var customerId = TryGetString(root, "customer", "id") ?? existingCustomerId ?? string.Empty;
        var (brand, last4) = ReadCardSummary(root);
        var expiry = TryGetString(root, "payment_source", "card", "expiry") ?? string.Empty;

        return new PayPalVaultCardResult(vaultId, customerId, brand ?? "CARD", last4 ?? "????", expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken);
        response.Dispose();
    }

    // ---------------------------------------------------------------- Reconciliation

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // PayPal caps each Transaction Search request at a 31-day window, so walk the range in chunks.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            int totalPages;
            do
            {
                var url = "/v1/reporting/transactions"
                    + $"?start_date={Uri.EscapeDataString(FormatDate(windowStart))}"
                    + $"&end_date={Uri.EscapeDataString(FormatDate(windowEnd))}"
                    + "&fields=all&page_size=500"
                    + $"&page={page}";

                using var response = await SendAsync(HttpMethod.Get, url, null, null, cancellationToken);
                using var doc = await ReadJsonAsync(response, cancellationToken);
                var root = doc.RootElement;

                totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var tpv) ? tpv : 1;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (detail.TryGetProperty("transaction_info", out var info))
                        {
                            results.Add(MapTransaction(info));
                        }
                    }
                }

                page++;
            }
            while (page <= totalPages);

            // Nudge past the inclusive window end to avoid duplicate rows on the boundary.
            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private static PayPalTransaction MapTransaction(JsonElement info)
    {
        var txnId = GetString(info, "transaction_id") ?? string.Empty;
        var status = GetString(info, "transaction_status");
        var eventCode = GetString(info, "transaction_event_code");
        var invoiceId = GetString(info, "invoice_id");
        var customField = GetString(info, "custom_field");

        decimal? amount = ReadMoney(info, "transaction_amount");
        string? currency = ReadCurrency(info, "transaction_amount");
        decimal? fee = ReadMoney(info, "fee_amount");

        DateTimeOffset? date = null;
        var dateStr = GetString(info, "transaction_initiation_date");
        if (dateStr is not null && DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            date = parsed;
        }

        return new PayPalTransaction(txnId, status, amount, currency, fee, invoiceId, customField, eventCode, date);
    }

    // ---------------------------------------------------------------- HTTP + auth plumbing

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string relativeUrl, object? body, string? requestId, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var response = await SendCoreAsync(method, relativeUrl, body, requestId, token, cancellationToken);

        // A cached token can be rejected if it expired early; refresh once and retry.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            token = await GetAccessTokenAsync(cancellationToken, forceRefresh: true);
            response = await SendCoreAsync(method, relativeUrl, body, requestId, token, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowPayPalErrorAsync(response, method, relativeUrl, cancellationToken);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method, string relativeUrl, object? body, string? requestId, string token, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, BaseUrl + relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _cachedToken;
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowPayPalErrorAsync(response, HttpMethod.Post, "/v1/oauth2/token", cancellationToken);
            }

            using var doc = await ReadJsonAsync(response, cancellationToken);
            var root = doc.RootElement;
            var accessToken = GetString(root, "access_token")
                ?? throw new PayPalApiException("PayPal token response did not contain an access_token.");
            var expiresIn = root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var s) ? s : 300;

            _cachedToken = accessToken;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task ThrowPayPalErrorAsync(HttpResponseMessage response, HttpMethod method, string relativeUrl, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var debugId = response.Headers.TryGetValues("Paypal-Debug-Id", out var ids) ? string.Join(",", ids) : null;

        string name = response.StatusCode.ToString();
        string message = raw;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            name = GetString(root, "name") ?? name;
            var baseMessage = GetString(root, "message") ?? name;
            debugId ??= GetString(root, "debug_id");

            var sb = new StringBuilder(baseMessage);
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    var issue = GetString(d, "issue");
                    var desc = GetString(d, "description");
                    if (issue is not null || desc is not null)
                    {
                        sb.Append(" [").Append(issue).Append(": ").Append(desc).Append(']');
                    }
                }
            }
            message = sb.ToString();
        }
        catch (JsonException)
        {
            message = Truncate(raw);
        }

        _logger.LogWarning("PayPal {Method} {Url} failed ({Status}): {Name} - {Message} (debug-id: {DebugId})",
            method, relativeUrl, (int)response.StatusCode, name, message, debugId);

        response.Dispose();
        throw new PayPalApiException($"PayPal API error ({(int)response.StatusCode}): {message}", debugId, name);
    }

    // ---------------------------------------------------------------- JSON helpers

    private object BuildCardBody(CardDetails card, bool includeSecurityCode)
    {
        var body = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["name"] = card.CardholderName
        };
        if (includeSecurityCode && !string.IsNullOrEmpty(card.SecurityCode))
        {
            body["security_code"] = card.SecurityCode;
        }
        if (card.BillingAddress is { } addr)
        {
            body["billing_address"] = new Dictionary<string, object?>
            {
                ["address_line_1"] = addr.AddressLine1,
                ["admin_area_2"] = addr.AdminArea2,
                ["admin_area_1"] = addr.AdminArea1,
                ["postal_code"] = addr.PostalCode,
                ["country_code"] = addr.CountryCode
            };
        }
        return body;
    }

    private Dictionary<string, object?> Money(decimal amount) => new()
    {
        ["currency_code"] = _settings.Currency,
        ["value"] = FormatAmount(amount)
    };

    private string FormatAmount(decimal amount)
    {
        var decimals = ZeroDecimalCurrencies.Contains(_settings.Currency) ? 0 : 2;
        return amount.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "Z";

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return JsonDocument.Parse("{}");
        }
        return JsonDocument.Parse(raw);
    }

    private static bool RequiresPayerAction(JsonElement root, string status)
    {
        if (status.Equals("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = GetString(link, "rel");
                if (rel is not null &&
                    (rel.Equals("payer-action", StringComparison.OrdinalIgnoreCase) ||
                     rel.Equals("approve", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static string? FindAuthorizationId(JsonElement root)
    {
        if (root.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.TryGetProperty("payments", out var payments) &&
                    payments.TryGetProperty("authorizations", out var auths) &&
                    auths.ValueKind == JsonValueKind.Array)
                {
                    foreach (var auth in auths.EnumerateArray())
                    {
                        var id = GetString(auth, "id");
                        if (id is not null)
                        {
                            return id;
                        }
                    }
                }
            }
        }
        return null;
    }

    private static (string? brand, string? last4) ReadCardSummary(JsonElement root)
    {
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            var brand = GetString(card, "brand") ?? GetString(card, "card_type");
            var last4 = GetString(card, "last_digits");
            return (brand, last4);
        }
        return (null, null);
    }

    private static decimal? ReadMoney(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var money) && money.TryGetProperty("value", out var value))
        {
            var raw = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            if (raw is not null && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            {
                return d;
            }
        }
        return null;
    }

    private static string? ReadCurrency(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var money))
        {
            return GetString(money, "currency_code");
        }
        return null;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? TryGetString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string Truncate(string value, int max = 500) =>
        value.Length <= max ? value : value.Substring(0, max) + "…";
}
