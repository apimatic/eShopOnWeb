using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Talks to PayPal's REST API directly (Orders v2, Payments v2, Vault v3, Reporting) following the
/// PayPal plugin's documented endpoints. Card details are used only to build the outgoing request
/// and are never persisted or logged.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private const int MaxReportingWindowDays = 31;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly PayPalTokenStore _tokenStore;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(
        HttpClient httpClient,
        PayPalSettings settings,
        PayPalTokenStore tokenStore,
        IAppLogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolveBaseUrl();

    // ---- Authorize (Orders v2) ----

    public async Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(
        CreateAuthorizationCommand command, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object cardOrToken = command.VaultId is not null
            ? Obj(("vault_id", command.VaultId))
            : BuildCard(command.Card!);

        var body = Obj(
            ("intent", "AUTHORIZE"),
            ("purchase_units", new[]
            {
                Obj(
                    ("reference_id", command.ReferenceId),
                    // The unique invoice id (not the small, reused order id) so reconciliation can
                    // line records up reliably on a shared account.
                    ("custom_id", command.InvoiceId),
                    ("invoice_id", command.InvoiceId),
                    ("amount", Obj(
                        ("currency_code", command.CurrencyCode),
                        ("value", command.Amount),
                        ("breakdown", Obj(
                            ("item_total", Money(command.CurrencyCode, command.Amount)))))),
                    ("items", command.Items.Select(i => Obj(
                        ("name", Truncate(i.Name, 127)),
                        ("quantity", i.Quantity.ToString(CultureInfo.InvariantCulture)),
                        ("unit_amount", Money(command.CurrencyCode, i.UnitAmount)))).ToArray()))
            }),
            ("payment_source", Obj(("card", cardOrToken))));

        using var response = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, idempotencyKey, cancellationToken);
        var root = response.RootElement;

        var orderId = root.GetProperty("id").GetString()!;
        var status = GetStringOrNull(root, "status");

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalChallengeRequiredException(
                $"PayPal order {orderId} needs shopper approval in a browser (status PAYER_ACTION_REQUIRED).");
        }

        var authorization = TryGetFirstAuthorization(root);

        // If the order is approved but not yet authorized, authorize it explicitly.
        if (authorization is null && string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            using var authorizeResponse = await SendAsync(
                HttpMethod.Post, $"/v2/checkout/orders/{orderId}/authorize", new Dictionary<string, object?>(),
                $"{idempotencyKey}-auth", cancellationToken);
            authorization = TryGetFirstAuthorization(authorizeResponse.RootElement);
        }

        if (authorization is null)
        {
            throw new PayPalApiException(
                $"PayPal order {orderId} did not yield an authorization (status {status ?? "unknown"}).",
                root.GetRawText());
        }

        var auth = authorization.Value;
        var authId = auth.GetProperty("id").GetString()!;
        var authStatus = GetStringOrNull(auth, "status") ?? "CREATED";
        var (amountValue, currency) = ReadAmount(auth, "amount") ?? (command.Amount, command.CurrencyCode);

        return new PayPalAuthorizationResult(orderId, authId, authStatus, amountValue, currency);
    }

    // ---- Authorization lifecycle (Payments v2) ----

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        var root = response.RootElement;
        var status = GetStringOrNull(root, "status") ?? "UNKNOWN";
        var amount = ReadAmountDecimal(root, "amount");
        return new PayPalAuthorizationDetails(authorizationId, status, amount.Amount, amount.Currency);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string amount, string currencyCode, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = Obj(
            ("amount", Money(currencyCode, amount)),
            ("final_capture", true));

        using var response = await SendAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, cancellationToken);
        var root = response.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = GetStringOrNull(root, "status") ?? "COMPLETED";
        var (gross, currency) = ReadAmountDecimal(root, "amount");

        decimal fee = 0m;
        decimal net = gross;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadAmountDecimal(breakdown, "gross_amount").Amount;
            fee = ReadAmountDecimal(breakdown, "paypal_fee").Amount;
            var netRead = ReadAmountDecimal(breakdown, "net_amount");
            net = netRead.Amount;
            if (!string.IsNullOrEmpty(netRead.Currency))
            {
                currency = netRead.Currency;
            }
        }

        return new PayPalCaptureResult(captureId, status, gross, fee, net, currency);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId, string amount, string currencyCode, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = Obj(("amount", Money(currencyCode, amount)));
        using var response = await SendAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, idempotencyKey, cancellationToken);
        var root = response.RootElement;

        var newId = root.GetProperty("id").GetString()!;
        var status = GetStringOrNull(root, "status") ?? "CREATED";
        var (value, currency) = ReadAmountDecimal(root, "amount");
        return new PayPalAuthorizationDetails(newId, status, value, string.IsNullOrEmpty(currency) ? currencyCode : currency);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, idempotencyKey, cancellationToken);
    }

    // ---- Refund (Payments v2) ----

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, string? amount, string currencyCode, string idempotencyKey, string? noteToPayer,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>();
        if (amount is not null)
        {
            body["amount"] = Money(currencyCode, amount);
        }
        if (!string.IsNullOrWhiteSpace(noteToPayer))
        {
            body["note_to_payer"] = noteToPayer;
        }

        using var response = await SendAsync(
            HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, idempotencyKey, cancellationToken);
        var root = response.RootElement;

        var refundId = root.GetProperty("id").GetString()!;
        var status = GetStringOrNull(root, "status") ?? "COMPLETED";
        var (value, currency) = ReadAmountDecimal(root, "amount");
        if (value == 0m && amount is not null)
        {
            value = decimal.Parse(amount, CultureInfo.InvariantCulture);
        }
        return new PayPalRefundResult(refundId, status, value, string.IsNullOrEmpty(currency) ? currencyCode : currency);
    }

    // ---- Vault (Vault v3) ----

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardDetails card, string? customerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = Obj(
            ("payment_source", Obj(("card", BuildCard(card, forVault: true)))));

        using var response = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, idempotencyKey, cancellationToken);
        var root = response.RootElement;

        var vaultId = root.GetProperty("id").GetString()!;
        string? returnedCustomerId = null;
        if (root.TryGetProperty("customer", out var customer) && customer.TryGetProperty("id", out var custId))
        {
            returnedCustomerId = custId.GetString();
        }

        string? brand = null, lastDigits = null, expiry = null, name = null;
        if (root.TryGetProperty("payment_source", out var source) && source.TryGetProperty("card", out var cardEl))
        {
            brand = GetStringOrNull(cardEl, "brand");
            lastDigits = GetStringOrNull(cardEl, "last_digits");
            expiry = GetStringOrNull(cardEl, "expiry");
            name = GetStringOrNull(cardEl, "name");
        }

        return new PayPalVaultedCard(vaultId, returnedCustomerId ?? customerId, brand, lastDigits, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken);
    }

    // ---- Reporting (Transaction Search) ----

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // Transaction Search accepts at most a 31-day window per request, so walk the range in chunks.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(MaxReportingWindowDays);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await ReadTransactionWindowAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ReadTransactionWindowAsync(
        DateTimeOffset start, DateTimeOffset end, List<PayPalTransaction> results, CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var query =
                $"?start_date={Uri.EscapeDataString(FormatReportingDate(start))}" +
                $"&end_date={Uri.EscapeDataString(FormatReportingDate(end))}" +
                $"&fields=transaction_info&page_size=100&page={page}";

            using var response = await SendAsync(HttpMethod.Get, $"/v1/reporting/transactions{query}", null, null, cancellationToken);
            var root = response.RootElement;

            totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var tpv) ? tpv : 1;

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info))
                    {
                        continue;
                    }

                    var transactionId = GetStringOrNull(info, "transaction_id");
                    if (transactionId is null)
                    {
                        continue;
                    }

                    var (amountValue, currency) = ReadAmountDecimalNullable(info, "transaction_amount");
                    results.Add(new PayPalTransaction(
                        TransactionId: transactionId,
                        Status: GetStringOrNull(info, "transaction_status"),
                        Amount: amountValue,
                        CurrencyCode: currency,
                        Date: ParseDate(GetStringOrNull(info, "transaction_initiation_date")),
                        InvoiceId: GetStringOrNull(info, "invoice_id"),
                        CustomField: GetStringOrNull(info, "custom_field")));
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // ---- HTTP plumbing ----

    private async Task<JsonDocument> SendAsync(
        HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = BuildRequest(method, path, body, requestId, token);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        // A stale token: refresh once and retry.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _tokenStore.Invalidate();
            token = await GetAccessTokenAsync(cancellationToken);
            using var retry = BuildRequest(method, path, body, requestId, token);
            response = await _httpClient.SendAsync(retry, cancellationToken);
        }

        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var debugId = response.Headers.TryGetValues("PayPal-Debug-Id", out var ids) ? string.Join(",", ids) : null;
                throw new PayPalApiException(
                    $"PayPal call {method} {path} failed with HTTP {(int)response.StatusCode}: {Summarize(text)}",
                    text, debugId, (int)response.StatusCode)
                {
                    IssueName = ExtractIssueName(text)
                };
            }

            return string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse("{}") : JsonDocument.Parse(text);
        }
        finally
        {
            response.Dispose();
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body, string? requestId, string token)
    {
        var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body is not null)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }
        return request;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        return await _tokenStore.GetTokenAsync(FetchTokenAsync, cancellationToken);
    }

    private async Task<(string Token, int ExpiresInSeconds)> FetchTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new PayPalApiException("PayPal ClientId/ClientSecret are not configured (PayPal:ClientId / PayPal:ClientSecret).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var debugId = response.Headers.TryGetValues("PayPal-Debug-Id", out var ids) ? string.Join(",", ids) : null;
            throw new PayPalApiException(
                $"PayPal OAuth token request failed with HTTP {(int)response.StatusCode}: {Summarize(text)}", text, debugId, (int)response.StatusCode);
        }

        using var doc = JsonDocument.Parse(text);
        var accessToken = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new PayPalApiException("PayPal OAuth response did not contain an access_token.", text);
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var v) ? v : 3000;
        _logger.LogInformation("Acquired PayPal access token (expires in {0}s).", expiresIn);
        return (accessToken, expiresIn);
    }

    // ---- JSON helpers ----

    private static Dictionary<string, object?> Obj(params (string Key, object? Value)[] pairs)
    {
        var dictionary = new Dictionary<string, object?>();
        foreach (var (key, value) in pairs)
        {
            if (value is not null)
            {
                dictionary[key] = value;
            }
        }
        return dictionary;
    }

    private static Dictionary<string, object?> Money(string currencyCode, string value) =>
        Obj(("currency_code", currencyCode), ("value", value));

    private Dictionary<string, object?> BuildCard(CardDetails card, bool forVault = false)
    {
        var dictionary = Obj(
            ("number", new string(card.Number.Where(char.IsDigit).ToArray())),
            ("expiry", card.Expiry),
            ("name", card.Name));

        if (!forVault && !string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            dictionary["security_code"] = card.SecurityCode;
        }

        if (card.BillingAddress is not null)
        {
            dictionary["billing_address"] = Obj(
                ("address_line_1", card.BillingAddress.AddressLine1),
                ("address_line_2", card.BillingAddress.AddressLine2),
                ("admin_area_1", card.BillingAddress.AdminArea1),
                ("admin_area_2", card.BillingAddress.AdminArea2),
                ("postal_code", card.BillingAddress.PostalCode),
                ("country_code", card.BillingAddress.CountryCode));
        }

        return dictionary;
    }

    private static JsonElement? TryGetFirstAuthorization(JsonElement orderRoot)
    {
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("authorizations", out var authorizations)
                && authorizations.ValueKind == JsonValueKind.Array
                && authorizations.GetArrayLength() > 0)
            {
                return authorizations[0];
            }
        }

        return null;
    }

    private static string? GetStringOrNull(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static (string Value, string Currency)? ReadAmount(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var amount))
        {
            return null;
        }
        var value = GetStringOrNull(amount, "value");
        var currency = GetStringOrNull(amount, "currency_code");
        return value is null ? null : (value, currency ?? string.Empty);
    }

    private static (decimal Amount, string Currency) ReadAmountDecimal(JsonElement parent, string property)
    {
        var raw = ReadAmount(parent, property);
        if (raw is null)
        {
            return (0m, string.Empty);
        }
        decimal.TryParse(raw.Value.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value);
        return (value, raw.Value.Currency);
    }

    private static (decimal? Amount, string? Currency) ReadAmountDecimalNullable(JsonElement parent, string property)
    {
        var raw = ReadAmount(parent, property);
        if (raw is null)
        {
            return (null, null);
        }
        return decimal.TryParse(raw.Value.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? (value, raw.Value.Currency)
            : (null, raw.Value.Currency);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string FormatReportingDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    private static string Summarize(string body) =>
        string.IsNullOrWhiteSpace(body) ? "(empty body)" : Truncate(body.Replace("\n", " ").Replace("\r", " "), 500);

    private static string? ExtractIssueName(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("details", out var details)
                && details.ValueKind == JsonValueKind.Array
                && details.GetArrayLength() > 0
                && details[0].TryGetProperty("issue", out var issue))
            {
                return issue.GetString();
            }
            if (doc.RootElement.TryGetProperty("name", out var name))
            {
                return name.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — nothing to extract.
        }
        return null;
    }
}
