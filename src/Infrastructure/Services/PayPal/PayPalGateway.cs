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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// The concrete PayPal REST client. Owns OAuth (with token caching), idempotency headers, base-url
/// resolution (honouring the optional override), and translation of PayPal errors into
/// <see cref="PayPalGatewayException"/>. Card numbers and CVVs are never logged.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    // Transaction Search allows at most a 31-day window per request; chunk conservatively.
    private static readonly TimeSpan MaxReportWindow = TimeSpan.FromDays(30);

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalGateway> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalGateway(HttpClient httpClient, PayPalSettings settings, IAppLogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_settings.ResolveBaseUrl());
        }
    }

    // ----- Orders v2 -----

    public async Task<PayPalOrderResult> CreateAuthorizationOrderAsync(
        CreateOrderCommand command, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var purchaseUnit = new Dictionary<string, object?>
        {
            ["invoice_id"] = command.InvoiceId,
            ["custom_id"] = command.CustomId,
            ["description"] = command.Description,
            ["amount"] = Money(command.Amount, command.CurrencyCode)
        };

        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[] { purchaseUnit }
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            idempotencyKey, fullRepresentation: true, cancellationToken);
        var root = doc.RootElement;
        return new PayPalOrderResult(GetString(root, "id")!, GetString(root, "status") ?? "CREATED");
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(
        string payPalOrderId, PayPalCardPaymentSource source, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCard(source) }
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", body,
            idempotencyKey, fullRepresentation: true, cancellationToken);
        var root = doc.RootElement;

        GuardAgainstChallenge(root);

        // Authorization lives at purchase_units[0].payments.authorizations[0].
        if (TryGetAuthorization(root, out var auth))
        {
            return new PayPalAuthorizationResult(
                GetString(auth, "id")!,
                GetString(auth, "status") ?? "CREATED",
                GetDate(auth, "expiration_time"));
        }

        throw new PayPalGatewayException(
            $"PayPal authorize for order {payPalOrderId} returned status '{GetString(root, "status")}' with no authorization.",
            (int)HttpStatusCode.BadGateway);
    }

    // ----- Payments v2 -----

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["final_capture"] = true };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body,
            idempotencyKey, fullRepresentation: true, cancellationToken);
        var root = doc.RootElement;

        decimal gross = 0m;
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = GetMoney(breakdown, "gross_amount") ?? 0m;
            fee = GetMoney(breakdown, "paypal_fee");
            net = GetMoney(breakdown, "net_amount");
        }
        if (gross == 0m)
        {
            gross = GetMoney(root, "amount") ?? 0m;
        }

        return new PayPalCaptureResult(GetString(root, "id")!, GetString(root, "status") ?? "COMPLETED", gross, fee, net);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Money(amount, currencyCode) };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body,
            idempotencyKey, fullRepresentation: true, cancellationToken);
        var root = doc.RootElement;
        return new PayPalAuthorizationResult(
            GetString(root, "id")!, GetString(root, "status") ?? "CREATED", GetDate(root, "expiration_time"));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null,
            idempotencyKey: null, fullRepresentation: false, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?>? body = amount is null
            ? null
            : new Dictionary<string, object?> { ["amount"] = Money(amount.Value, currencyCode) };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body,
            idempotencyKey, fullRepresentation: true, cancellationToken);
        var root = doc.RootElement;
        var refundAmount = GetMoney(root, "amount") ?? amount ?? 0m;
        return new PayPalRefundResult(GetString(root, "id")!, GetString(root, "status") ?? "COMPLETED", refundAmount);
    }

    // ----- Vault v3 -----

    public async Task<PayPalVaultCardResult> VaultCardAsync(
        PayPalRawCard card, string merchantCustomerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Vaulting a card is a two-step exchange: create a setup token from the raw card (approved
        // immediately for cards, no browser step), then exchange it for a durable payment token.
        // PayPal generates a customer id on the setup token which we chain to the payment token so
        // both belong to the same customer. (merchantCustomerId is intentionally not sent as-is:
        // PayPal rejects some merchant_customer_id values, and shopper ownership is tracked in eShop.)
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildRawCard(card) }
        };
        _ = merchantCustomerId;

        string setupTokenId;
        string? setupCustomerId;
        using (var setupDoc = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody,
            $"{idempotencyKey}-setup", fullRepresentation: true, cancellationToken))
        {
            var setupRoot = setupDoc.RootElement;
            setupTokenId = GetString(setupRoot, "id")
                ?? throw new PayPalGatewayException("PayPal setup token creation returned no id.", (int)HttpStatusCode.BadGateway);
            setupCustomerId = setupRoot.TryGetProperty("customer", out var setupCustomer)
                ? GetString(setupCustomer, "id")
                : null;
        }

        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?> { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
            }
        };
        if (!string.IsNullOrWhiteSpace(setupCustomerId))
        {
            tokenBody["customer"] = new Dictionary<string, object?> { ["id"] = setupCustomerId };
        }

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody,
            $"{idempotencyKey}-token", fullRepresentation: true, cancellationToken);
        var root = doc.RootElement;

        string vaultId = GetString(root, "id")!;
        string? brand = null, last4 = null, expiry = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = GetString(c, "brand");
            last4 = GetString(c, "last_digits");
            expiry = GetString(c, "expiry");
        }
        return new PayPalVaultCardResult(vaultId, brand, last4, expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null,
            idempotencyKey: null, fullRepresentation: false, cancellationToken);
    }

    // ----- Transaction Search v1 -----

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // De-duplicate across page/window boundaries by transaction id + event code.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<PayPalTransaction>();

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            var totalPages = 1;
            do
            {
                var query = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatRfc3339(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatRfc3339(windowEnd))}" +
                    "&fields=all&balance_affecting_records_only=N&page_size=100" +
                    $"&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, query, null,
                    idempotencyKey: null, fullRepresentation: false, cancellationToken);
                var root = doc.RootElement;

                if (root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number)
                {
                    totalPages = tp.GetInt32();
                }

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info))
                        {
                            continue;
                        }

                        var txn = ParseTransaction(info);
                        var dedupeKey = $"{txn.TransactionId}|{txn.EventCode}";
                        if (seen.Add(dedupeKey))
                        {
                            results.Add(txn);
                        }
                    }
                }

                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd == to ? to : windowEnd;
            if (windowEnd == to)
            {
                break;
            }
        }

        return results;
    }

    private static PayPalTransaction ParseTransaction(JsonElement info) =>
        new(
            TransactionId: GetString(info, "transaction_id") ?? string.Empty,
            EventCode: GetString(info, "transaction_event_code"),
            Status: GetString(info, "transaction_status"),
            GrossAmount: GetMoney(info, "transaction_amount"),
            FeeAmount: GetMoney(info, "fee_amount"),
            CurrencyCode: TryGetCurrency(info, "transaction_amount"),
            InitiationDate: GetDate(info, "transaction_initiation_date"),
            InvoiceId: GetString(info, "invoice_id"),
            CustomField: GetString(info, "custom_field"));

    // ----- request/JSON building -----

    private static object BuildCard(PayPalCardPaymentSource source)
    {
        if (!string.IsNullOrWhiteSpace(source.VaultId))
        {
            return new Dictionary<string, object?> { ["vault_id"] = source.VaultId };
        }

        if (source.Card is null)
        {
            throw new PayPalGatewayException("No card details or vault id supplied for authorization.", 400);
        }

        return BuildRawCard(source.Card);
    }

    private static Dictionary<string, object?> BuildRawCard(PayPalRawCard card)
    {
        var dict = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name
        };

        if (card.BillingAddress is { } address)
        {
            dict["billing_address"] = new Dictionary<string, object?>
            {
                ["address_line_1"] = address.AddressLine1,
                ["address_line_2"] = address.AddressLine2,
                ["admin_area_2"] = address.AdminArea2,
                ["admin_area_1"] = address.AdminArea1,
                ["postal_code"] = address.PostalCode,
                ["country_code"] = address.CountryCode
            };
        }

        return dict;
    }

    private static Dictionary<string, object?> Money(decimal amount, string currencyCode) => new()
    {
        ["currency_code"] = currencyCode,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    // ----- HTTP plumbing -----

    private async Task<JsonDocument> SendAsync(
        HttpMethod method, string path, object? body, string? idempotencyKey, bool fullRepresentation,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (fullRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var debugId = response.Headers.TryGetValues("PayPal-Debug-Id", out var ids) ? ids.FirstOrDefault() : null;

        if (!response.IsSuccessStatusCode)
        {
            var (issue, message) = ParseError(payload);
            _logger.LogWarning(
                $"PayPal {method} {path} failed: {(int)response.StatusCode} {issue} (debug-id {debugId}).");
            throw new PayPalGatewayException(
                $"PayPal {method} {path} failed ({(int)response.StatusCode}): {issue ?? message ?? response.ReasonPhrase}",
                (int)response.StatusCode, issue, debugId);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            // 204 No Content (e.g. void, delete): return an empty JSON document.
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(payload);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var debugId = response.Headers.TryGetValues("PayPal-Debug-Id", out var ids) ? ids.FirstOrDefault() : null;

            if (!response.IsSuccessStatusCode)
            {
                var (issue, message) = ParseError(payload);
                throw new PayPalGatewayException(
                    $"PayPal OAuth token request failed ({(int)response.StatusCode}): {issue ?? message ?? response.ReasonPhrase}",
                    (int)response.StatusCode, issue, debugId);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            _accessToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3000;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            _logger.LogInformation($"Obtained PayPal access token, valid ~{expiresIn}s.");
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void GuardAgainstChallenge(JsonElement root)
    {
        var status = GetString(root, "status");
        var requiresAction = string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase);

        if (!requiresAction && root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            requiresAction = links.EnumerateArray().Any(l =>
            {
                var rel = GetString(l, "rel");
                return string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rel, "approve", StringComparison.OrdinalIgnoreCase);
            });
        }

        if (requiresAction)
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. a 3-D Secure step-up). " +
                "This integration is headless and does not perform a browser approval round-trip.");
        }
    }

    private static (string? Issue, string? Message) ParseError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var name = GetString(root, "name") ?? GetString(root, "error");
            var message = GetString(root, "message") ?? GetString(root, "error_description");

            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                var first = details.EnumerateArray().FirstOrDefault();
                var issue = GetString(first, "issue");
                if (!string.IsNullOrWhiteSpace(issue))
                {
                    return (issue, message);
                }
            }

            return (name, message);
        }
        catch (JsonException)
        {
            return (null, payload.Length > 200 ? payload[..200] : payload);
        }
    }

    // ----- JSON reading helpers -----

    private static bool TryGetAuthorization(JsonElement root, out JsonElement authorization)
    {
        authorization = default;
        if (root.TryGetProperty("purchase_units", out var units) && units.ValueKind == JsonValueKind.Array)
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.TryGetProperty("payments", out var payments)
                    && payments.TryGetProperty("authorizations", out var auths)
                    && auths.ValueKind == JsonValueKind.Array)
                {
                    var first = auths.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object)
                    {
                        authorization = first;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }
        return null;
    }

    private static decimal? GetMoney(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var money)
            && money.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return null;
    }

    private static string? TryGetCurrency(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var money))
        {
            return GetString(money, "currency_code");
        }
        return null;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        if (raw is not null && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var date))
        {
            return date;
        }
        return null;
    }

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
