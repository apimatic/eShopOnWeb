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
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST API implementation of <see cref="IPayPalPaymentGateway"/>. Uses a typed
/// <see cref="HttpClient"/> (base address configured from <see cref="PayPalSettings.ResolveBaseUrl"/>)
/// and a shared <see cref="PayPalTokenStore"/> for OAuth client-credentials tokens.
/// </summary>
public class PayPalClient : IPayPalPaymentGateway
{
    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly PayPalTokenStore _tokenStore;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(HttpClient http, PayPalSettings settings,
        PayPalTokenStore tokenStore, ILogger<PayPalClient> logger)
    {
        _http = http;
        _settings = settings;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Orders / authorize

    public Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardNode = BuildCardNode(card);
        return CreateAndReadAuthorizationAsync(amount, currency, cardNode, idempotencyKey, cancellationToken);
    }

    public Task<AuthorizationResult> AuthorizeWithVaultAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardNode = new Dictionary<string, object?> { ["vault_id"] = vaultId };
        return CreateAndReadAuthorizationAsync(amount, currency, cardNode, idempotencyKey, cancellationToken);
    }

    private async Task<AuthorizationResult> CreateAndReadAuthorizationAsync(decimal amount, string currency,
        Dictionary<string, object?> cardNode, string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["amount"] = Money(amount, currency)
                }
            },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = cardNode
            }
        };

        using var doc = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", body,
            idempotencyKey, preferRepresentation: true, cancellationToken);
        var root = doc.RootElement;

        var orderStatus = GetString(root, "status");
        GuardAgainstChallenge(root, orderStatus);

        var payPalOrderId = GetString(root, "id")
            ?? throw new PayPalApiException(502, null, null, "PayPal order response missing id.");

        var authorization = TryGetAuthorizationElement(root)
            ?? throw new PayPalApiException(502, null, null,
                $"PayPal did not return an authorization for order {payPalOrderId} (order status '{orderStatus}').");

        return ReadAuthorization(authorization, payPalOrderId, root);
    }

    public async Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}",
            body: null, idempotencyKey: null, preferRepresentation: false, cancellationToken);
        return ReadAuthorization(doc.RootElement, payPalOrderId: null, orderRoot: null, authorizationId);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Money(amount, currency) };
        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body,
            idempotencyKey: null, preferRepresentation: true, cancellationToken);
        return ReadAuthorization(doc.RootElement, payPalOrderId: null, orderRoot: null, authorizationId);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", body: null,
            idempotencyKey: null, preferRepresentation: false, cancellationToken);
        // 204 No Content on success; nothing to read.
    }

    // ---------------------------------------------------------------- Capture

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Money(amount, currency),
            ["final_capture"] = true
        };

        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body,
            idempotencyKey, preferRepresentation: true, cancellationToken);
        var root = doc.RootElement;

        var captureId = GetString(root, "id")
            ?? throw new PayPalApiException(502, null, null, "PayPal capture response missing id.");
        var status = GetString(root, "status") ?? "UNKNOWN";

        decimal gross = amount;
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            gross = ReadMoney(breakdown, "gross_amount") ?? amount;
            fee = ReadMoney(breakdown, "paypal_fee");
            net = ReadMoney(breakdown, "net_amount");
        }

        return new CaptureResult(captureId, status, gross, fee, net);
    }

    // ---------------------------------------------------------------- Refund

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?>? body = amount.HasValue
            ? new Dictionary<string, object?> { ["amount"] = Money(amount.Value, currency) }
            : new Dictionary<string, object?>(); // empty body = full refund

        using var doc = await SendJsonAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body,
            idempotencyKey, preferRepresentation: true, cancellationToken);
        var root = doc.RootElement;

        var refundId = GetString(root, "id")
            ?? throw new PayPalApiException(502, null, null, "PayPal refund response missing id.");
        var status = GetString(root, "status") ?? "UNKNOWN";
        var refunded = ReadMoney(root, "amount") ?? amount ?? 0m;

        return new RefundResult(refundId, status, refunded);
    }

    // ---------------------------------------------------------------- Vault (save card)

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default)
    {
        // Step 1: create a setup token holding the raw card.
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildCardNode(card, includeSecurityCode: false)
            }
        };
        string setupTokenId;
        using (var setupDoc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupBody,
            idempotencyKey: Guid.NewGuid().ToString("N"), preferRepresentation: false, cancellationToken))
        {
            setupTokenId = GetString(setupDoc.RootElement, "id")
                ?? throw new PayPalApiException(502, null, null, "PayPal setup-token response missing id.");
        }

        // Step 2: exchange the setup token for a permanent payment (vault) token.
        var tokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?>
                {
                    ["id"] = setupTokenId,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        using var doc = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenBody,
            idempotencyKey: Guid.NewGuid().ToString("N"), preferRepresentation: false, cancellationToken);
        var root = doc.RootElement;

        var vaultId = GetString(root, "id")
            ?? throw new PayPalApiException(502, null, null, "PayPal payment-token response missing id.");

        string? brand = null, last4 = null, expiry = null;
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var c))
        {
            brand = GetString(c, "brand");
            last4 = GetString(c, "last_digits");
            expiry = GetString(c, "expiry");
        }

        return new VaultCardResult(vaultId, brand, last4, expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendJsonAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}",
            body: null, idempotencyKey: null, preferRepresentation: false, cancellationToken);
        // 204 No Content on success.
    }

    // ---------------------------------------------------------------- Reconciliation

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();

        // PayPal limits each request to a 31-day window; chunk the range and page through each window.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to) windowEnd = to;

            int page = 1;
            int totalPages;
            do
            {
                var query = $"?start_date={Uri.EscapeDataString(FormatRfc3339(windowStart))}" +
                            $"&end_date={Uri.EscapeDataString(FormatRfc3339(windowEnd))}" +
                            $"&fields=transaction_info&page_size=500&page={page}";

                using var doc = await SendJsonAsync(HttpMethod.Get, "/v1/reporting/transactions" + query,
                    body: null, idempotencyKey: null, preferRepresentation: false, cancellationToken);
                var root = doc.RootElement;

                totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var tpv) ? tpv : 1;

                if (root.TryGetProperty("transaction_details", out var details)
                    && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info)) continue;
                        var txnId = GetString(info, "transaction_id");
                        if (txnId is null) continue;

                        results.Add(new PayPalTransaction(
                            TransactionId: txnId,
                            Status: GetString(info, "transaction_status"),
                            Amount: ReadMoney(info, "transaction_amount"),
                            CurrencyCode: ReadMoneyCurrency(info, "transaction_amount"),
                            InitiationDate: ParseDate(GetString(info, "transaction_initiation_date")),
                            EventCode: GetString(info, "transaction_event_code")));
                    }
                }

                page++;
            } while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results;
    }

    // ---------------------------------------------------------------- JSON helpers

    private Dictionary<string, object?> BuildCardNode(CardDetails card, bool includeSecurityCode = true)
    {
        var node = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["name"] = card.Name,
            ["billing_address"] = new Dictionary<string, object?>
            {
                ["address_line_1"] = card.BillingAddress.AddressLine1,
                ["admin_area_2"] = card.BillingAddress.AdminArea2,
                ["admin_area_1"] = card.BillingAddress.AdminArea1,
                ["postal_code"] = card.BillingAddress.PostalCode,
                ["country_code"] = card.BillingAddress.CountryCode
            },
            ["attributes"] = new Dictionary<string, object?>
            {
                ["verification"] = new Dictionary<string, object?> { ["method"] = "SCA_WHEN_REQUIRED" }
            }
        };
        if (includeSecurityCode)
        {
            node["security_code"] = card.SecurityCode;
        }
        return node;
    }

    private static Dictionary<string, object?> Money(decimal amount, string currency) => new()
    {
        ["currency_code"] = currency,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private void GuardAgainstChallenge(JsonElement root, string? orderStatus)
    {
        if (string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure challenge). " +
                "This integration does not perform a browser approval round-trip; the payment cannot be completed server-side.");
        }

        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (string.Equals(GetString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PayPalChallengeRequiredException(
                        "PayPal returned a 'payer-action' link indicating the shopper must approve this payment in a browser " +
                        "(3-D Secure challenge). This integration does not perform a browser approval round-trip.");
                }
            }
        }
    }

    private static JsonElement? TryGetAuthorizationElement(JsonElement orderRoot)
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
                        return auth;
                    }
                }
            }
        }
        return null;
    }

    private AuthorizationResult ReadAuthorization(JsonElement auth, string? payPalOrderId, JsonElement? orderRoot,
        string? fallbackAuthId = null)
    {
        var authId = GetString(auth, "id") ?? fallbackAuthId
            ?? throw new PayPalApiException(502, null, null, "PayPal authorization missing id.");
        var status = GetString(auth, "status") ?? "UNKNOWN";
        var expires = ParseDate(GetString(auth, "expiration_time"));

        string? brand = null, last4 = null;
        if (orderRoot is { } root && root.TryGetProperty("payment_source", out var ps)
            && ps.TryGetProperty("card", out var card))
        {
            brand = GetString(card, "brand");
            last4 = GetString(card, "last_digits");
        }

        return new AuthorizationResult(payPalOrderId ?? string.Empty, authId, status, expires, brand, last4);
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? ReadMoney(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var money)
            && money.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return null;
    }

    private static string? ReadMoneyCurrency(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var money) ? GetString(money, "currency_code") : null;

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;

    private static string FormatRfc3339(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- HTTP plumbing

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, bool preferRepresentation, CancellationToken cancellationToken)
    {
        // One automatic retry after clearing the cached token on a 401.
        for (var attempt = 0; ; attempt++)
        {
            var token = await _tokenStore.GetAsync(FetchTokenAsync, cancellationToken);

            // Relative to the configured base address, so a BaseUrl override (possibly with a
            // path prefix) is honoured verbatim for every call.
            using var request = new HttpRequestMessage(method, new Uri(path.TrimStart('/'), UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (idempotencyKey is not null)
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
            }
            if (preferRepresentation)
            {
                request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            }
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _tokenStore.Invalidate();
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException(response.StatusCode, payload);
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                // e.g. 204 No Content — return an empty object document so callers can dispose safely.
                return JsonDocument.Parse("{}");
            }

            return JsonDocument.Parse(payload);
        }
    }

    private PayPalApiException BuildApiException(HttpStatusCode status, string payload)
    {
        string? name = null, message = null, debugId = null, details = null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            name = GetString(root, "name");
            message = GetString(root, "message");
            debugId = GetString(root, "debug_id");
            if (root.TryGetProperty("details", out var det) && det.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var d in det.EnumerateArray())
                {
                    var issue = GetString(d, "issue");
                    var desc = GetString(d, "description");
                    if (issue is not null || desc is not null)
                    {
                        parts.Add($"{issue}: {desc}".Trim(':', ' '));
                    }
                }
                if (parts.Count > 0) details = string.Join("; ", parts);
            }
        }
        catch (JsonException)
        {
            // non-JSON error body; fall through with raw payload
        }

        var summary = message ?? name ?? "PayPal API error";
        if (details is not null) summary += $" ({details})";
        _logger.LogWarning("PayPal API error {Status} {Name} debug_id={DebugId}: {Summary}",
            (int)status, name, debugId, summary);

        // Prefer the specific issue name (from details) over the generic top-level name so that
        // callers (e.g. capture/reauthorize handling) can branch on it.
        var issueName = FirstIssue(payload) ?? name;
        return new PayPalApiException((int)status, issueName, debugId,
            $"PayPal returned {(int)status}: {summary}");
    }

    private static string? FirstIssue(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("details", out var det)
                && det.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in det.EnumerateArray())
                {
                    if (d.TryGetProperty("issue", out var issue) && issue.ValueKind == JsonValueKind.String)
                    {
                        return issue.GetString();
                    }
                }
            }
        }
        catch (JsonException) { }
        return null;
    }

    private async Task<(string token, int expiresInSeconds)> FetchTokenAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("v1/oauth2/token", UriKind.Relative));
        var basic = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException(response.StatusCode, payload);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var accessToken = GetString(root, "access_token")
            ?? throw new PayPalApiException(502, null, null, "PayPal token response missing access_token.");
        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var ev) ? ev : 3000;
        return (accessToken, expiresIn);
    }
}
