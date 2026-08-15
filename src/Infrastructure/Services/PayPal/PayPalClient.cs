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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// HTTP implementation of <see cref="IPayPalClient"/> against the PayPal REST API. Owns OAuth token
/// acquisition/caching, idempotency headers, amount formatting and translation of PayPal error
/// payloads into domain exceptions. Full card details flow through but are never persisted or logged.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private static readonly HashSet<string> ZeroDecimalCurrencies =
        new(StringComparer.OrdinalIgnoreCase) { "JPY", "KRW", "HUF", "TWD", "CLP", "VND" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, PayPalSettings settings, IAppLogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    // ---------------------------------------------------------------------
    // Orders + authorization
    // ---------------------------------------------------------------------

    public Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(decimal amount, string currencyCode,
        PayPalCardDetails card, string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new Dictionary<string, object?> { ["card"] = BuildCardObject(card) };
        return CreateAndAuthorizeAsync(amount, currencyCode, invoiceId, paymentSource, idempotencyKey, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultedCardAsync(decimal amount, string currencyCode,
        string vaultId, string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new Dictionary<string, object?>
        {
            ["card"] = new Dictionary<string, object?> { ["vault_id"] = vaultId }
        };
        return CreateAndAuthorizeAsync(amount, currencyCode, invoiceId, paymentSource, idempotencyKey, cancellationToken);
    }

    private async Task<PayPalAuthorizationResult> CreateAndAuthorizeAsync(decimal amount, string currencyCode,
        string invoiceId, Dictionary<string, object?> paymentSource, string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["invoice_id"] = invoiceId,
                    ["amount"] = BuildAmount(amount, currencyCode)
                }
            },
            ["payment_source"] = paymentSource
        };

        var headers = new List<(string, string)>
        {
            ("PayPal-Request-Id", idempotencyKey),
            ("Prefer", "return=representation")
        };

        using var doc = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, headers, cancellationToken);
        var root = doc.RootElement;

        GuardNoBuyerChallenge(root, "authorizing the order");

        var payPalOrderId = GetString(root, "id")
            ?? throw new PayPalApiException("PayPal order response did not include an id.", HttpStatusCode.OK, root.ToString());

        var (authId, authStatus) = TryExtractAuthorization(root);

        // If the order was only created/approved (no authorization yet), authorize it explicitly.
        if (authId is null)
        {
            using var authDoc = await SendJsonAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
                new Dictionary<string, object?>(), headers, cancellationToken);
            GuardNoBuyerChallenge(authDoc.RootElement, "authorizing the order");
            (authId, authStatus) = TryExtractAuthorization(authDoc.RootElement);
            if (authId is null)
            {
                throw new PayPalApiException(
                    "PayPal did not return an authorization for the order.", HttpStatusCode.OK, authDoc.RootElement.ToString());
            }
        }

        var instrument = DescribeInstrument(root);
        return new PayPalAuthorizationResult(payPalOrderId, authId!, authStatus ?? "CREATED", instrument);
    }

    public async Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}",
            null, null, cancellationToken);
        var root = doc.RootElement;
        var status = GetString(root, "status") ?? "UNKNOWN";
        DateTimeOffset? expiry = null;
        if (root.TryGetProperty("expiration_time", out var exp) && exp.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(exp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            expiry = parsed;
        }
        return new PayPalAuthorizationState(authorizationId, status, expiry);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = BuildAmount(amount, currencyCode) };
        var headers = new List<(string, string)>
        {
            ("PayPal-Request-Id", idempotencyKey),
            ("Prefer", "return=representation")
        };

        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, headers, cancellationToken);
        var root = doc.RootElement;
        var newId = GetString(root, "id")
            ?? throw new PayPalApiException("Reauthorize did not return a new authorization id.", HttpStatusCode.OK, root.ToString());
        var status = GetString(root, "status") ?? "CREATED";
        return new PayPalAuthorizationResult(string.Empty, newId, status, null);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currencyCode, string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = BuildAmount(amount, currencyCode),
            ["invoice_id"] = invoiceId,
            ["final_capture"] = true
        };
        var headers = new List<(string, string)> { ("PayPal-Request-Id", idempotencyKey) };

        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body, headers, cancellationToken);
        var root = doc.RootElement;

        var captureId = GetString(root, "id")
            ?? throw new PayPalApiException("Capture did not return an id.", HttpStatusCode.OK, root.ToString());
        var status = GetString(root, "status") ?? "COMPLETED";

        decimal gross = amount, fee = 0m, net = amount;
        var currency = currencyCode;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = GetAmountValue(breakdown, "gross_amount") ?? gross;
            fee = GetAmountValue(breakdown, "paypal_fee") ?? 0m;
            net = GetAmountValue(breakdown, "net_amount") ?? (gross - fee);
            currency = GetAmountCurrency(breakdown, "gross_amount") ?? currency;
        }

        return new PayPalCaptureResult(captureId, status, gross, fee, net, currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        // Void returns 204 (no body) or 200 (voided representation); both are success.
        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", null, null, cancellationToken);
    }

    // ---------------------------------------------------------------------
    // Refunds
    // ---------------------------------------------------------------------

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, string invoiceId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["invoice_id"] = invoiceId };
        if (amount is decimal value)
        {
            body["amount"] = BuildAmount(value, currencyCode);
        }
        var headers = new List<(string, string)> { ("PayPal-Request-Id", idempotencyKey) };

        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body, headers, cancellationToken);
        var root = doc.RootElement;

        var refundId = GetString(root, "id")
            ?? throw new PayPalApiException("Refund did not return an id.", HttpStatusCode.OK, root.ToString());
        var status = GetString(root, "status") ?? "COMPLETED";
        var refundedAmount = GetAmountValue(root, "amount") ?? amount ?? 0m;
        var currency = GetAmountCurrency(root, "amount") ?? currencyCode;

        return new PayPalRefundResult(refundId, status, refundedAmount, currency);
    }

    // ---------------------------------------------------------------------
    // Vault (saved cards)
    // ---------------------------------------------------------------------

    public async Task<PayPalVaultResult> VaultCardAsync(PayPalCardDetails card, string? existingCustomerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Step 1: create a setup token from the raw card (browser-free for standard sandbox cards).
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCardObject(card) }
        };
        if (!string.IsNullOrEmpty(existingCustomerId))
        {
            setupBody["customer"] = new Dictionary<string, object?> { ["id"] = existingCustomerId };
        }

        var setupHeaders = new List<(string, string)> { ("PayPal-Request-Id", $"{idempotencyKey}-setup") };
        using var setupDoc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody, setupHeaders, cancellationToken);
        var setupRoot = setupDoc.RootElement;
        GuardNoBuyerChallenge(setupRoot, "saving the card");

        var setupTokenId = GetString(setupRoot, "id")
            ?? throw new PayPalApiException("Setup token response did not include an id.", HttpStatusCode.OK, setupRoot.ToString());
        var customerId = setupRoot.TryGetProperty("customer", out var setupCustomer)
            ? GetString(setupCustomer, "id")
            : existingCustomerId;

        // Step 2: exchange the approved setup token for a permanent payment token.
        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?> { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
            }
        };
        var tokenHeaders = new List<(string, string)> { ("PayPal-Request-Id", $"{idempotencyKey}-token") };
        using var tokenDoc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody, tokenHeaders, cancellationToken);
        var tokenRoot = tokenDoc.RootElement;

        var vaultId = GetString(tokenRoot, "id")
            ?? throw new PayPalApiException("Payment token response did not include an id.", HttpStatusCode.OK, tokenRoot.ToString());
        if (tokenRoot.TryGetProperty("customer", out var tokenCustomer))
        {
            customerId = GetString(tokenCustomer, "id") ?? customerId;
        }

        string brand = "CARD", last4 = "", expiry = card.Expiry;
        string? name = card.Name;
        if (tokenRoot.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand") ?? brand;
            last4 = GetString(cardEl, "last_digits") ?? last4;
            expiry = GetString(cardEl, "expiry") ?? expiry;
            name = GetString(cardEl, "name") ?? name;
        }

        return new PayPalVaultResult(vaultId, customerId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, BuildUri($"/v3/vault/payment-tokens/{vaultId}"));
        await AuthorizeRequestAsync(request, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        // Treat an already-absent token as success so delete is idempotent.
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new PayPalApiException($"Deleting vaulted card failed with {(int)response.StatusCode}.",
                response.StatusCode, raw);
        }
    }

    // ---------------------------------------------------------------------
    // Reporting / reconciliation
    // ---------------------------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // PayPal caps a single Transaction Search request at 31 days — chunk to cover the whole range.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var query =
                    $"?start_date={Uri.EscapeDataString(FormatReportDate(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatReportDate(windowEnd))}" +
                    $"&fields=transaction_info&page_size=500&page={page}";

                using var doc = await SendJsonAsync(HttpMethod.Get, "/v1/reporting/transactions" + query,
                    null, null, cancellationToken);
                var root = doc.RootElement;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (detail.TryGetProperty("transaction_info", out var info))
                        {
                            results.Add(ParseTransaction(info));
                        }
                    }
                }

                totalPages = root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number
                    ? tp.GetInt32()
                    : 1;
                page++;
            }
            while (page <= totalPages && !cancellationToken.IsCancellationRequested);

            windowStart = windowEnd;
        }

        return results;
    }

    private static PayPalTransaction ParseTransaction(JsonElement info)
    {
        var id = GetString(info, "transaction_id") ?? string.Empty;
        var status = GetString(info, "transaction_status");
        var amount = GetAmountValue(info, "transaction_amount");
        var currency = GetAmountCurrency(info, "transaction_amount");
        var invoiceId = GetString(info, "invoice_id");
        var custom = GetString(info, "custom_field");
        DateTimeOffset? date = null;
        if (info.TryGetProperty("transaction_initiation_date", out var d) && d.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            date = parsed;
        }
        return new PayPalTransaction(id, status, amount, currency, date, invoiceId, custom);
    }

    // ---------------------------------------------------------------------
    // HTTP plumbing
    // ---------------------------------------------------------------------

    private Uri BuildUri(string path) => new(_settings.ResolveBaseUrl() + path);

    private async Task AuthorizeRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body,
        IEnumerable<(string Name, string Value)>? headers, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path));
        await AuthorizeRequestAsync(request, cancellationToken);

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal {Method} {Path} failed: {StatusCode}. Body: {Body}",
                method.Method, path, (int)response.StatusCode, Truncate(raw));
            throw new PayPalApiException($"PayPal {method} {path} failed with status {(int)response.StatusCode}.",
                response.StatusCode, raw);
        }

        return string.IsNullOrWhiteSpace(raw) ? JsonDocument.Parse("{}") : JsonDocument.Parse(raw);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _cachedToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PayPalApiException($"PayPal token request failed with status {(int)response.StatusCode}.",
                    response.StatusCode, raw);
            }

            using var doc = JsonDocument.Parse(raw);
            var token = doc.RootElement.GetProperty("access_token").GetString()
                ?? throw new PayPalApiException("PayPal token response had no access_token.", response.StatusCode, raw);
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetInt32()
                : 3000;

            _cachedToken = token;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // ---------------------------------------------------------------------
    // JSON helpers
    // ---------------------------------------------------------------------

    private Dictionary<string, object?> BuildAmount(decimal amount, string currencyCode) => new()
    {
        ["currency_code"] = currencyCode,
        ["value"] = FormatAmount(amount, currencyCode)
    };

    private static string FormatAmount(decimal amount, string currencyCode)
    {
        var format = ZeroDecimalCurrencies.Contains(currencyCode) ? "0" : "0.00";
        return decimal.Round(amount, ZeroDecimalCurrencies.Contains(currencyCode) ? 0 : 2, MidpointRounding.AwayFromZero)
            .ToString(format, CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, object?> BuildCardObject(PayPalCardDetails card)
    {
        var billing = new Dictionary<string, object?>
        {
            ["address_line_1"] = string.IsNullOrWhiteSpace(card.BillingAddressLine1) ? "1 Main St" : card.BillingAddressLine1,
            ["admin_area_2"] = string.IsNullOrWhiteSpace(card.BillingCity) ? "San Jose" : card.BillingCity,
            ["admin_area_1"] = string.IsNullOrWhiteSpace(card.BillingState) ? "CA" : card.BillingState,
            ["postal_code"] = string.IsNullOrWhiteSpace(card.BillingPostalCode) ? "95131" : card.BillingPostalCode,
            ["country_code"] = string.IsNullOrWhiteSpace(card.BillingCountryCode) ? "US" : card.BillingCountryCode
        };
        if (!string.IsNullOrWhiteSpace(card.BillingAddressLine2))
        {
            billing["address_line_2"] = card.BillingAddressLine2;
        }

        var cardObject = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["billing_address"] = billing
        };
        if (!string.IsNullOrWhiteSpace(card.SecurityCode)) cardObject["security_code"] = card.SecurityCode;
        if (!string.IsNullOrWhiteSpace(card.Name)) cardObject["name"] = card.Name;
        return cardObject;
    }

    private static (string? AuthId, string? Status) TryExtractAuthorization(JsonElement orderRoot)
    {
        if (orderRoot.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.TryGetProperty("payments", out var payments)
                    && payments.TryGetProperty("authorizations", out var auths)
                    && auths.ValueKind == JsonValueKind.Array)
                {
                    foreach (var auth in auths.EnumerateArray())
                    {
                        var id = GetString(auth, "id");
                        if (!string.IsNullOrEmpty(id))
                        {
                            return (id, GetString(auth, "status"));
                        }
                    }
                }
            }
        }
        return (null, null);
    }

    private static string? DescribeInstrument(JsonElement orderRoot)
    {
        if (orderRoot.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            var brand = GetString(card, "brand");
            var last4 = GetString(card, "last_digits");
            if (!string.IsNullOrEmpty(last4))
            {
                return $"{brand ?? "Card"} ending {last4}";
            }
        }
        return null;
    }

    private void GuardNoBuyerChallenge(JsonElement root, string action)
    {
        var status = GetString(root, "status");
        var challenge = status == "PAYER_ACTION_REQUIRED";
        if (!challenge && root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            challenge = links.EnumerateArray().Any(l => GetString(l, "rel") == "payer-action");
        }

        if (challenge)
        {
            _logger.LogWarning($"PayPal required a buyer approval challenge while {action}.");
            throw new PaymentChallengeRequiredException(
                $"PayPal requires the shopper to approve this card in a browser (3-D Secure) while {action}. " +
                "This integration does not perform browser approval round-trips; use a card that clears without a challenge.");
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? GetAmountValue(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var amount) && amount.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static string? GetAmountCurrency(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var amount) ? GetString(amount, "currency_code") : null;

    private static string FormatReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Truncate(string value) =>
        value.Length <= 2000 ? value : value.Substring(0, 2000) + "…";
}
