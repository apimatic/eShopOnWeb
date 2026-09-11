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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST implementation of <see cref="IPaymentGateway"/>. All endpoint shapes and headers
/// follow the PayPal Payments API (Orders v2, Payments v2, Vault v3, Transaction Search v1).
/// Full card details are only ever sent to PayPal — never persisted or logged by this client.
/// </summary>
public class PayPalClient : IPaymentGateway
{
    // Transaction Search allows a maximum window of 31 days per request.
    private const int MaxReportWindowDays = 31;

    private readonly HttpClient _http;
    private readonly PayPalConfiguration _config;
    private readonly IAppLogger<PayPalClient> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PayPalClient(HttpClient http, PayPalConfiguration config, IAppLogger<PayPalClient> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<GatewayAuthorizationResult> AuthorizeAsync(
        GatewayAmount amount, GatewayCardDetails? card, string? vaultId,
        string orderReference, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object paymentSource;
        if (!string.IsNullOrEmpty(vaultId))
        {
            paymentSource = new { card = new { vault_id = vaultId } };
        }
        else if (card is not null)
        {
            paymentSource = new { card = BuildCardPayload(card) };
        }
        else
        {
            throw new PaymentOperationException("Provide either card details or a saved card to authorize the payment.");
        }

        var createBody = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = orderReference,
                    invoice_id = orderReference,
                    custom_id = orderReference,
                    amount = MoneyPayload(amount)
                }
            },
            payment_source = paymentSource
        };

        using var createDoc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", createBody,
            idempotencyKey: idempotencyKey + "-create", cancellationToken: cancellationToken);
        var createRoot = createDoc!.RootElement;

        var providerOrderId = createRoot.GetProperty("id").GetString()!;
        var createStatus = GetString(createRoot, "status");
        GuardAgainstApprovalChallenge(createRoot, createStatus);

        // If the order already carries an authorization (some flows authorize on create), use it.
        if (TryReadAuthorization(createRoot, out var inlineAuth))
        {
            return inlineAuth!;
        }

        // Otherwise, place the hold explicitly.
        using var authDoc = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{providerOrderId}/authorize",
            body: new { }, idempotencyKey: idempotencyKey + "-auth", cancellationToken: cancellationToken);
        var authRoot = authDoc!.RootElement;
        GuardAgainstApprovalChallenge(authRoot, GetString(authRoot, "status"));

        if (!TryReadAuthorization(authRoot, out var authResult))
        {
            throw new PaymentGatewayException(
                "PayPal did not return an authorization for the order.",
                debugId: GetString(authRoot, "debug_id"));
        }

        return authResult! with { ProviderOrderId = providerOrderId };
    }

    public async Task<GatewayAuthorizationResult> ReauthorizeAsync(
        string authorizationId, GatewayAmount amount, CancellationToken cancellationToken = default)
    {
        var body = new { amount = MoneyPayload(amount) };
        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body,
            idempotencyKey: null, cancellationToken: cancellationToken);
        var root = doc!.RootElement;

        return new GatewayAuthorizationResult(
            ProviderOrderId: string.Empty,
            AuthorizationId: root.GetProperty("id").GetString()!,
            Status: GetString(root, "status") ?? "CREATED",
            ExpiresAt: GetDateTime(root, "expiration_time"));
    }

    public async Task<GatewayCaptureResult> CaptureAsync(
        string authorizationId, GatewayAmount amount, string orderReference,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Do not resend invoice_id here: the authorizing transaction already carries it (the
        // account requires globally-unique invoice ids), and reporting reports that value for the
        // capture. final_capture releases any remaining hold.
        var body = new
        {
            amount = MoneyPayload(amount),
            final_capture = true
        };

        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body,
            idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
        var root = doc!.RootElement;

        var captureId = root.GetProperty("id").GetString()!;
        var status = GetString(root, "status") ?? "COMPLETED";
        var gross = amount.Value;
        decimal? fee = null;
        decimal? net = null;
        var currency = amount.CurrencyCode;

        if (root.TryGetProperty("amount", out var amountEl))
        {
            gross = ParseMoney(amountEl) ?? gross;
            currency = GetString(amountEl, "currency_code") ?? currency;
        }

        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            if (breakdown.TryGetProperty("gross_amount", out var grossEl))
            {
                gross = ParseMoney(grossEl) ?? gross;
            }
            if (breakdown.TryGetProperty("paypal_fee", out var feeEl))
            {
                fee = ParseMoney(feeEl);
            }
            if (breakdown.TryGetProperty("net_amount", out var netEl))
            {
                net = ParseMoney(netEl);
            }
        }

        return new GatewayCaptureResult(captureId, status, gross, fee, net, currency);
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", body: null,
            idempotencyKey: idempotencyKey, cancellationToken: cancellationToken, allowEmptyResponse: true);
    }

    public async Task<GatewayRefundResult> RefundAsync(
        string captureId, GatewayAmount? amount, string orderReference,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // custom_id ties the refund back to the order for reconciliation; invoice_id is inherited
        // from the capture in reporting, so it is not resent (it must stay globally unique).
        object body = amount is null
            ? new { custom_id = orderReference }
            : new { amount = MoneyPayload(amount), custom_id = orderReference };

        using var doc = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body,
            idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
        var root = doc!.RootElement;

        var refundId = root.GetProperty("id").GetString()!;
        var status = GetString(root, "status") ?? "COMPLETED";
        var refundedAmount = amount?.Value ?? 0m;
        var currency = amount?.CurrencyCode ?? _config.Currency;
        if (root.TryGetProperty("amount", out var amountEl))
        {
            refundedAmount = ParseMoney(amountEl) ?? refundedAmount;
            currency = GetString(amountEl, "currency_code") ?? currency;
        }

        return new GatewayRefundResult(refundId, status, refundedAmount, currency);
    }

    public async Task<GatewayVaultResult> VaultCardAsync(
        GatewayCardDetails card, string? customerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object body = string.IsNullOrEmpty(customerId)
            ? new { payment_source = new { card = BuildCardPayload(card) } }
            : new { customer = new { id = customerId }, payment_source = new { card = BuildCardPayload(card) } };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body,
            idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);
        var root = doc!.RootElement;

        var vaultId = root.GetProperty("id").GetString()!;
        string? returnedCustomerId = null;
        if (root.TryGetProperty("customer", out var customerEl))
        {
            returnedCustomerId = GetString(customerEl, "id");
        }

        string brand = "CARD";
        string last4 = "0000";
        string? expiry = null;
        string? name = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand") ?? brand;
            last4 = GetString(cardEl, "last_digits") ?? last4;
            expiry = GetString(cardEl, "expiry");
            name = GetString(cardEl, "name");
        }

        return new GatewayVaultResult(vaultId, returnedCustomerId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}",
            body: null, idempotencyKey: null, cancellationToken: cancellationToken, allowEmptyResponse: true);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();

        // The provider caps a request at 31 days, so cover the whole range in windows.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(MaxReportWindowDays);
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
        DateTimeOffset windowStart, DateTimeOffset windowEnd, List<GatewayTransaction> results, CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var page = 1;
        int totalPages;

        do
        {
            var query =
                $"?start_date={Uri.EscapeDataString(FormatRfc3339(windowStart))}" +
                $"&end_date={Uri.EscapeDataString(FormatRfc3339(windowEnd))}" +
                $"&fields=transaction_info&balance_affecting_records_only=N" +
                $"&page_size={pageSize}&page={page}";

            using var doc = await SendAsync(HttpMethod.Get, "/v1/reporting/transactions" + query,
                body: null, idempotencyKey: null, cancellationToken: cancellationToken);
            var root = doc!.RootElement;

            totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var tpv) ? tpv : 1;

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info))
                    {
                        continue;
                    }

                    decimal? amount = null;
                    string? currency = null;
                    if (info.TryGetProperty("transaction_amount", out var amt))
                    {
                        amount = ParseMoney(amt);
                        currency = GetString(amt, "currency_code");
                    }

                    results.Add(new GatewayTransaction(
                        TransactionId: GetString(info, "transaction_id") ?? string.Empty,
                        EventCode: GetString(info, "transaction_event_code"),
                        Status: GetString(info, "transaction_status") ?? string.Empty,
                        Amount: amount,
                        CurrencyCode: currency,
                        InvoiceId: GetString(info, "invoice_id"),
                        CustomField: GetString(info, "custom_field"),
                        InitiationDate: GetDateTime(info, "transaction_initiation_date")));
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // --- Access token ---

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, ResolveUri("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_config.ClientId}:{_config.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _http.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildException("obtain an access token", (int)response.StatusCode, payload);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            _accessToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var eiv) ? eiv : 3000;
            // Refresh a minute early to avoid using an about-to-expire token.
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // --- HTTP plumbing ---

    private async Task<JsonDocument?> SendAsync(
        HttpMethod method, string path, object? body, string? idempotencyKey,
        CancellationToken cancellationToken, bool allowEmptyResponse = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, ResolveUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Request the full resource so ids / statuses / breakdowns are present.
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException($"{method} {path}", (int)response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            if (allowEmptyResponse)
            {
                return null;
            }
            throw new PaymentGatewayException($"PayPal returned an empty response for {method} {path}.");
        }

        return JsonDocument.Parse(payload);
    }

    private Uri ResolveUri(string path) => new(_config.ResolveBaseUrl() + path);

    private PaymentGatewayException BuildException(string action, int statusCode, string payload)
    {
        string? debugId = null;
        string? message = null;
        var issues = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            debugId = GetString(root, "debug_id");
            message = GetString(root, "message") ?? GetString(root, "error_description") ?? GetString(root, "error");

            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var issue = GetString(detail, "issue");
                    var description = GetString(detail, "description");
                    if (issue is not null || description is not null)
                    {
                        issues.Add(string.Join(": ", new[] { issue, description }.Where(s => !string.IsNullOrEmpty(s))));
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with the raw payload truncated.
        }

        var summary = message ?? (payload.Length > 300 ? payload[..300] : payload);
        var issuesText = issues.Count > 0 ? $" Issues: {string.Join("; ", issues)}." : string.Empty;
        var retryable = statusCode == 429 || statusCode >= 500;

        // Log the debug id (never the request body / card data) so failures can be traced.
        _logger.LogWarning($"PayPal call failed to {action}: HTTP {statusCode}. {summary}{issuesText} debug_id={debugId}");

        return new PaymentGatewayException(
            $"PayPal failed to {action} (HTTP {statusCode}): {summary}.{issuesText}",
            providerStatusCode: statusCode,
            debugId: debugId,
            issues: issues,
            retryable: retryable);
    }

    private void GuardAgainstApprovalChallenge(JsonElement root, string? status)
    {
        // A card that needs the shopper to approve in a browser (e.g. a 3DS challenge) surfaces as
        // PAYER_ACTION_REQUIRED with a payer-action link. This integration is browserless by design.
        if (!string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? link = null;
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in links.EnumerateArray())
            {
                if (string.Equals(GetString(l, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    link = GetString(l, "href");
                }
            }
        }

        throw new PaymentGatewayException(
            "PayPal returned a challenge that requires the shopper to approve the payment in a browser " +
            $"(status PAYER_ACTION_REQUIRED{(link is null ? "" : $", action: {link}")}). " +
            "This browserless integration cannot complete such a payment.",
            debugId: GetString(root, "debug_id"),
            issues: new[] { "PAYER_ACTION_REQUIRED" });
    }

    /// <summary>Reads the first authorization from an order response's purchase_units[].payments.authorizations[].</summary>
    private static bool TryReadAuthorization(JsonElement orderRoot, out GatewayAuthorizationResult? result)
    {
        result = null;
        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments) ||
                !payments.TryGetProperty("authorizations", out var auths) ||
                auths.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var auth in auths.EnumerateArray())
            {
                if (!auth.TryGetProperty("id", out var idEl))
                {
                    continue;
                }

                result = new GatewayAuthorizationResult(
                    ProviderOrderId: GetString(orderRoot, "id") ?? string.Empty,
                    AuthorizationId: idEl.GetString()!,
                    Status: GetString(auth, "status") ?? "CREATED",
                    ExpiresAt: GetDateTime(auth, "expiration_time"));
                return true;
            }
        }

        return false;
    }

    // --- payload builders / parsers ---

    private object MoneyPayload(GatewayAmount amount) => new
    {
        currency_code = amount.CurrencyCode,
        value = amount.Value.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static object BuildCardPayload(GatewayCardDetails card)
    {
        var payload = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode
        };
        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            payload["name"] = card.Name;
        }
        if (card.BillingAddress is not null)
        {
            var addr = new Dictionary<string, object?>
            {
                ["country_code"] = card.BillingAddress.CountryCode
            };
            AddIfPresent(addr, "address_line_1", card.BillingAddress.AddressLine1);
            AddIfPresent(addr, "address_line_2", card.BillingAddress.AddressLine2);
            AddIfPresent(addr, "admin_area_2", card.BillingAddress.AdminArea2);
            AddIfPresent(addr, "admin_area_1", card.BillingAddress.AdminArea1);
            AddIfPresent(addr, "postal_code", card.BillingAddress.PostalCode);
            payload["billing_address"] = addr;
        }
        return payload;
    }

    private static void AddIfPresent(IDictionary<string, object?> dict, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            dict[key] = value;
        }
    }

    private static string FormatRfc3339(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? ParseMoney(JsonElement moneyElement)
    {
        if (moneyElement.ValueKind == JsonValueKind.Object &&
            moneyElement.TryGetProperty("value", out var v) &&
            v.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static DateTimeOffset? GetDateTime(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        if (raw is not null && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            return value;
        }
        return null;
    }
}
