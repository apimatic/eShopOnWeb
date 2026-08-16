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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The one and only PayPal integration point. Talks to the Orders v2, Payments v2, Vault v3 and
/// Transaction Search v1 REST APIs, exactly as documented, and translates them into the neutral
/// <see cref="IPaymentGateway"/> contract. Card numbers pass straight through to PayPal and are never
/// stored or written to logs.
/// </summary>
public class PayPalClient : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Maximum window Transaction Search accepts per request.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int SearchPageSize = 100;
    private const int MaxSearchPages = 1000; // safety backstop against runaway paging

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalClient> _logger;
    private readonly string _baseUrl;

    // The access token is shared across the (transient) typed-client instances so we don't request a
    // fresh one on every call. Keyed by client id in case the credentials ever change at runtime.
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);
    private static string? _accessToken;
    private static string? _accessTokenClientId;
    private static DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings, IAppLogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _baseUrl = _settings.ResolveBaseUrl();
    }

    public string Currency => _settings.Currency;

    // ---------------------------------------------------------------- Orders / authorize ----

    public Task<GatewayAuthorizationResult> AuthorizeWithCardAsync(string reference, decimal amount,
        CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return CreateAuthorizationAsync(reference, amount, BuildCardBody(card), idempotencyKey, cancellationToken);
    }

    public Task<GatewayAuthorizationResult> AuthorizeWithVaultedCardAsync(string reference, decimal amount,
        string vaultId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardBody = new Dictionary<string, object?>
        {
            ["vault_id"] = vaultId,
            ["stored_credential"] = new
            {
                payment_initiator = "CUSTOMER",
                payment_type = "UNSCHEDULED",
                usage = "SUBSEQUENT"
            }
        };
        return CreateAuthorizationAsync(reference, amount, cardBody, idempotencyKey, cancellationToken);
    }

    private async Task<GatewayAuthorizationResult> CreateAuthorizationAsync(string reference, decimal amount,
        Dictionary<string, object?> cardBody, string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = "default",
                    invoice_id = reference,
                    custom_id = reference,
                    amount = Money(amount)
                }
            },
            payment_source = new Dictionary<string, object?> { ["card"] = cardBody }
        };

        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        using var doc = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body, headers, cancellationToken);
        var root = doc.RootElement;

        var orderId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal did not return an order id.");
        var status = GetString(root, "status") ?? "UNKNOWN";

        EnsureNoBrowserChallenge(root, status);

        var authorization = FindAuthorization(root);
        if (authorization is null && string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            // The card was approved but not yet authorized in one step — authorize explicitly.
            authorization = await AuthorizeApprovedOrderAsync(orderId, idempotencyKey, cancellationToken);
        }

        if (authorization is null)
        {
            throw new PaymentGatewayException(
                $"PayPal order {orderId} returned status {status} with no authorization to act on.");
        }

        var auth = authorization.Value;
        var (brand, last4) = ReadCardSummary(root);
        return new GatewayAuthorizationResult(orderId, auth.Id, auth.Status, auth.ExpiresAt, brand, last4);
    }

    private async Task<AuthorizationInfo?> AuthorizeApprovedOrderAsync(string orderId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = $"{idempotencyKey}-authorize",
            ["Prefer"] = "return=representation"
        };
        using var doc = await SendJsonAsync(HttpMethod.Post, $"/v2/checkout/orders/{orderId}/authorize",
            new { }, headers, cancellationToken);
        return FindAuthorization(doc.RootElement);
    }

    // ---------------------------------------------------------------- Capture / reauth / void ----

    public async Task<GatewayCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new { amount = Money(amount), final_capture = true };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body, headers, cancellationToken);
        var root = doc.RootElement;

        var captureId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal did not return a capture id.");
        var status = GetString(root, "status") ?? "UNKNOWN";
        var gross = ReadMoney(root, "amount") ?? amount;

        decimal? fee = null, net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadMoney(breakdown, "gross_amount") ?? gross;
            fee = ReadMoney(breakdown, "paypal_fee");
            net = ReadMoney(breakdown, "net_amount");
        }

        return new GatewayCaptureResult(captureId, status, gross, fee, net);
    }

    public async Task<GatewayAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new { amount = Money(amount) };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, headers, cancellationToken);
        var root = doc.RootElement;

        var newAuthId = GetString(root, "id") ?? authorizationId;
        var status = GetString(root, "status") ?? "UNKNOWN";
        var expiresAt = ReadDate(root, "expiration_time");
        return new GatewayAuthorizationResult(string.Empty, newAuthId, status, expiresAt);
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string> { ["PayPal-Request-Id"] = idempotencyKey };
        await SendNoResultAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", null, headers, cancellationToken);
    }

    // ---------------------------------------------------------------- Refund ----

    public async Task<GatewayRefundResult> RefundAsync(string captureId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object body = amount is null ? new { } : new { amount = Money(amount.Value) };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body, headers, cancellationToken);
        var root = doc.RootElement;

        var refundId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal did not return a refund id.");
        var status = GetString(root, "status") ?? "UNKNOWN";
        var gross = ReadMoney(root, "amount") ?? amount ?? 0m;

        var totalRefunded = gross;
        if (root.TryGetProperty("seller_payable_breakdown", out var breakdown))
        {
            gross = ReadMoney(breakdown, "gross_amount") ?? gross;
            totalRefunded = ReadMoney(breakdown, "total_refunded_amount") ?? gross;
        }

        return new GatewayRefundResult(refundId, status, gross, totalRefunded);
    }

    // ---------------------------------------------------------------- Vault ----

    public async Task<GatewayVaultResult> VaultCardAsync(CardDetails card, string? customerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCardBody(card) }
        };
        if (!string.IsNullOrEmpty(customerId))
        {
            body["customer"] = new { id = customerId };
        }

        var headers = new Dictionary<string, string> { ["PayPal-Request-Id"] = idempotencyKey };

        using var doc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", body, headers, cancellationToken);
        var root = doc.RootElement;

        var vaultId = GetString(root, "id") ?? throw new PaymentGatewayException("PayPal did not return a vault token id.");
        string? returnedCustomerId = null;
        if (root.TryGetProperty("customer", out var customer))
        {
            returnedCustomerId = GetString(customer, "id");
        }

        string brand = "Card", last4 = "****", expiry = card.ExpiryYearMonth;
        string? name = card.CardholderName;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var cardEl))
        {
            brand = GetString(cardEl, "brand") ?? brand;
            last4 = GetString(cardEl, "last_digits") ?? last4;
            expiry = GetString(cardEl, "expiry") ?? expiry;
            name = GetString(cardEl, "name") ?? name;
        }

        return new GatewayVaultResult(vaultId, returnedCustomerId, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendNoResultAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken);
    }

    // ---------------------------------------------------------------- Transaction search ----

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();

        var windowStart = from;
        do
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await CollectWindowAsync(windowStart, windowEnd, results, cancellationToken);

            if (windowEnd >= to)
            {
                break;
            }
            windowStart = windowEnd;
        }
        while (windowStart < to);

        return results;
    }

    private async Task CollectWindowAsync(DateTimeOffset start, DateTimeOffset end,
        List<GatewayTransaction> results, CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var startParam = Uri.EscapeDataString(FormatRfc3339(start));
            var endParam = Uri.EscapeDataString(FormatRfc3339(end));
            var path = $"/v1/reporting/transactions?start_date={startParam}&end_date={endParam}" +
                       $"&fields=transaction_info&page_size={SearchPageSize}&page={page}";

            using var doc = await SendJsonAsync(HttpMethod.Get, path, null, null, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (!detail.TryGetProperty("transaction_info", out var info))
                    {
                        continue;
                    }

                    var (amount, currency) = ReadTransactionAmount(info);
                    results.Add(new GatewayTransaction(
                        GetString(info, "transaction_id") ?? string.Empty,
                        GetString(info, "transaction_event_code"),
                        GetString(info, "transaction_status"),
                        amount,
                        currency,
                        ReadDate(info, "transaction_initiation_date"),
                        GetString(info, "invoice_id"),
                        GetString(info, "custom_field")));
                }
            }

            totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var tpv) ? tpv : 1;
            page++;
        }
        while (page <= totalPages && page <= MaxSearchPages);
    }

    // ---------------------------------------------------------------- HTTP plumbing ----

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (IsTokenFresh())
        {
            return _accessToken!;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (IsTokenFresh())
            {
                return _accessToken!;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildException(response.StatusCode, payload, "Failed to obtain a PayPal access token");
            }

            using var doc = JsonDocument.Parse(payload);
            var token = GetString(doc.RootElement, "access_token")
                ?? throw new PaymentGatewayException("PayPal token response did not contain an access token.");
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var s) ? s : 300;

            _accessToken = token;
            _accessTokenClientId = _settings.ClientId;
            // Refresh a minute before the real expiry.
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private bool IsTokenFresh() =>
        _accessToken is not null
        && _accessTokenClientId == _settings.ClientId
        && DateTimeOffset.UtcNow < _accessTokenExpiresAt;

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body,
        IDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        var payload = await SendCoreAsync(method, path, body, headers, cancellationToken);
        return string.IsNullOrWhiteSpace(payload)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(payload);
    }

    private async Task SendNoResultAsync(HttpMethod method, string path, object? body,
        IDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        await SendCoreAsync(method, path, body, headers, cancellationToken);
    }

    private async Task<string> SendCoreAsync(HttpMethod method, string path, object? body,
        IDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, responseBody, $"PayPal {method} {path} failed");
        }

        return responseBody;
    }

    private PaymentGatewayException BuildException(HttpStatusCode statusCode, string payload, string context)
    {
        string? name = null, message = null, debugId = null;
        var issues = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message");
            debugId = GetString(root, "debug_id");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    var issue = GetString(d, "issue");
                    var description = GetString(d, "description");
                    if (issue is not null)
                    {
                        issues.Add(description is not null ? $"{issue} ({description})" : issue);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — fall through with the raw text.
        }

        var primaryIssue = issues.Count > 0 ? issues[0] : name;
        var detail = new StringBuilder(context).Append(": ");
        detail.Append(name ?? "error").Append(" - ").Append(message ?? payload);
        if (issues.Count > 0)
        {
            detail.Append(" [").Append(string.Join("; ", issues)).Append(']');
        }
        if (debugId is not null)
        {
            detail.Append(" (debug_id ").Append(debugId).Append(')');
        }

        _logger.LogWarning($"PayPal error {(int)statusCode}: {detail}");
        return new PaymentGatewayException(detail.ToString(), primaryIssue, debugId, (int)statusCode);
    }

    private void EnsureNoBrowserChallenge(JsonElement root, string status)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApprovalRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (PAYER_ACTION_REQUIRED). " +
                "This browser-less integration cannot complete such a challenge.");
        }

        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var rel = GetString(link, "rel");
                if (string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rel, "approve", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PaymentApprovalRequiredException(
                        "PayPal returned a browser approval step for this card payment; this browser-less integration cannot complete it.");
                }
            }
        }
    }

    private static AuthorizationInfo? FindAuthorization(JsonElement root)
    {
        if (!root.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("authorizations", out var auths)
                && auths.ValueKind == JsonValueKind.Array)
            {
                foreach (var auth in auths.EnumerateArray())
                {
                    var id = GetString(auth, "id");
                    if (id is null)
                    {
                        continue;
                    }
                    return new AuthorizationInfo(id, GetString(auth, "status") ?? "CREATED", ReadDate(auth, "expiration_time"));
                }
            }
        }
        return null;
    }

    private static (string? Brand, string? Last4) ReadCardSummary(JsonElement root)
    {
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var card))
        {
            return (GetString(card, "brand"), GetString(card, "last_digits"));
        }
        return (null, null);
    }

    // Build the card object, omitting any null/empty optional fields so we never send explicit nulls
    // (System.Text.Json writes null dictionary values even with WhenWritingNull, and PayPal rejects them).
    private static Dictionary<string, object?> BuildCardBody(CardDetails card)
    {
        var body = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.ExpiryYearMonth,
            ["security_code"] = card.SecurityCode
        };
        AddIfPresent(body, "name", card.CardholderName);
        body["billing_address"] = BuildAddress(card.BillingAddress);
        return body;
    }

    private static Dictionary<string, object?> BuildAddress(CardBillingAddress address)
    {
        var body = new Dictionary<string, object?> { ["country_code"] = address.CountryCode };
        AddIfPresent(body, "address_line_1", address.AddressLine1);
        AddIfPresent(body, "address_line_2", address.AddressLine2);
        AddIfPresent(body, "admin_area_2", address.City);
        AddIfPresent(body, "admin_area_1", address.State);
        AddIfPresent(body, "postal_code", address.PostalCode);
        return body;
    }

    private static void AddIfPresent(Dictionary<string, object?> body, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            body[key] = value;
        }
    }

    private object Money(decimal amount) => new
    {
        currency_code = _settings.Currency,
        value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static (decimal? Amount, string? Currency) ReadTransactionAmount(JsonElement info)
    {
        if (info.TryGetProperty("transaction_amount", out var amount))
        {
            var value = ReadMoney(amount, null);
            var currency = GetString(amount, "currency_code");
            return (value, currency);
        }
        return (null, null);
    }

    private static decimal? ReadMoney(JsonElement parent, string? property)
    {
        JsonElement money = parent;
        if (property is not null)
        {
            if (!parent.TryGetProperty(property, out money))
            {
                return null;
            }
        }
        var value = GetString(money, "value");
        return value is not null && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
    }

    private static DateTimeOffset? ReadDate(JsonElement parent, string property)
    {
        var value = GetString(parent, property);
        return value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dto)
            ? dto
            : null;
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private readonly record struct AuthorizationInfo(string Id, string Status, DateTimeOffset? ExpiresAt);
}
