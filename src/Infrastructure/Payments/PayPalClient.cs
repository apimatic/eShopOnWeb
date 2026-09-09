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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The concrete PayPal REST integration. Owns OAuth token acquisition/caching, request construction
/// per the PayPal Orders v2, Payments v2, Vault v3 and Transaction Search v1 APIs, idempotency headers
/// and error translation. Raw card data is only ever forwarded to PayPal — never persisted or logged.
/// </summary>
public class PayPalClient : IPayPalPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly string _baseUrl;

    // Token cache is process-wide: the typed HttpClient (and thus this class) is transient, but the
    // OAuth credentials are fixed for the process, so one shared, short-lived token serves all requests.
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);
    private static string? _accessToken;
    private static DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _baseUrl = _settings.ResolveBaseUrl();

        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal:ClientId and PayPal:ClientSecret must be configured (via user-secrets or environment).");
        }
    }

    public string Currency => _settings.Currency;

    // ---------------------------------------------------------------------------------------------
    // OAuth 2.0 client-credentials token, cached until shortly before expiry.
    // ---------------------------------------------------------------------------------------------
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

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await BuildApiExceptionAsync("Failed to obtain PayPal access token", response, body);
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            _accessToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3000;
            // Refresh a minute early to avoid using a token that expires mid-request.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Authorize: create a PayPal order with intent=AUTHORIZE and the card (raw or vaulted) as the
    // payment source. For a direct card this processes the hold in one call; if the order is not
    // auto-authorized we follow up with an explicit authorize call.
    // ---------------------------------------------------------------------------------------------
    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var card = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(request.VaultId))
        {
            card["vault_id"] = request.VaultId;
        }
        else if (request.Card is not null)
        {
            PopulateCard(card, request.Card);
        }
        else
        {
            throw new ArgumentException("A card or a saved-card vault id is required to authorize.");
        }

        var purchaseUnit = new Dictionary<string, object?>
        {
            ["amount"] = new { currency_code = Currency, value = FormatAmount(request.Amount) }
        };
        if (!string.IsNullOrWhiteSpace(request.InvoiceId)) purchaseUnit["invoice_id"] = request.InvoiceId;
        if (!string.IsNullOrWhiteSpace(request.CustomId)) purchaseUnit["custom_id"] = request.CustomId;
        if (!string.IsNullOrWhiteSpace(request.Description)) purchaseUnit["description"] = request.Description;

        var payload = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[] { purchaseUnit },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = card }
        };

        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = request.IdempotencyKey,
            ["Prefer"] = "return=representation"
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", payload, headers, cancellationToken);
        var order = doc.RootElement;
        var status = order.TryGetProperty("status", out var s) ? s.GetString() : null;

        if (status == "PAYER_ACTION_REQUIRED")
        {
            throw new PayPalChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This integration does not perform a browser approval round-trip.");
        }

        var payPalOrderId = order.GetProperty("id").GetString()!;

        if (TryExtractAuthorization(order, out var auth))
        {
            return Build(payPalOrderId, auth);
        }

        // Not auto-authorized (order is CREATED/APPROVED): authorize explicitly.
        using var authDoc = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
            new { }, new Dictionary<string, string>
            {
                ["PayPal-Request-Id"] = $"{request.IdempotencyKey}-auth",
                ["Prefer"] = "return=representation"
            }, cancellationToken);

        if (authDoc.RootElement.TryGetProperty("status", out var s2) && s2.GetString() == "PAYER_ACTION_REQUIRED")
        {
            throw new PayPalChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure).");
        }

        if (!TryExtractAuthorization(authDoc.RootElement, out auth))
        {
            throw new PayPalApiException("PayPal did not return an authorization for the order.", 500, null, null);
        }

        return Build(payPalOrderId, auth);

        static PayPalAuthorizationResult Build(string orderId, JsonElement authorization) => new()
        {
            PayPalOrderId = orderId,
            AuthorizationId = authorization.GetProperty("id").GetString()!,
            Status = authorization.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
            ExpiresAt = ReadDate(authorization, "expiration_time")
        };
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, bool finalCapture, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["amount"] = new { currency_code = Currency, value = FormatAmount(amount) },
            ["final_capture"] = finalCapture
        };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            payload, headers, cancellationToken);
        var capture = doc.RootElement;

        var result = new PayPalCaptureResult
        {
            CaptureId = capture.GetProperty("id").GetString()!,
            Status = capture.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
            GrossAmount = amount
        };

        if (capture.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            if (TryReadMoney(breakdown, "gross_amount", out var gross)) result.GrossAmount = gross;
            if (TryReadMoney(breakdown, "paypal_fee", out var fee)) result.PayPalFee = fee;
            if (TryReadMoney(breakdown, "net_amount", out var net)) result.NetAmount = net;
        }

        return result;
    }

    public async Task<PayPalReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["amount"] = new { currency_code = Currency, value = FormatAmount(amount) }
        };
        var headers = new Dictionary<string, string> { ["Prefer"] = "return=representation" };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            payload, headers, cancellationToken);
        var auth = doc.RootElement;

        return new PayPalReauthorizationResult
        {
            AuthorizationId = auth.GetProperty("id").GetString()!,
            Status = auth.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
            ExpiresAt = ReadDate(auth, "expiration_time")
        };
    }

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void",
            null, null, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object? payload = amount is null
            ? new { }
            : new { amount = new { currency_code = Currency, value = FormatAmount(amount.Value) } };

        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        using var doc = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund",
            payload, headers, cancellationToken);
        var refund = doc.RootElement;

        var result = new PayPalRefundResult
        {
            RefundId = refund.GetProperty("id").GetString()!,
            Status = refund.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
            Amount = amount ?? 0m
        };
        if (TryReadMoney(refund, "amount", out var refundedAmount))
        {
            result.Amount = refundedAmount;
        }
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Vault: save a card directly (the sandbox business account is enabled for direct card vaulting),
    // and delete a saved card.
    // ---------------------------------------------------------------------------------------------
    public async Task<VaultedCard> VaultCardAsync(CardDetails cardDetails, string? payPalCustomerId, CancellationToken cancellationToken = default)
    {
        var card = new Dictionary<string, object?>();
        PopulateCard(card, cardDetails);

        var payload = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = card }
        };
        if (!string.IsNullOrWhiteSpace(payPalCustomerId))
        {
            payload["customer"] = new { id = payPalCustomerId };
        }

        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = Guid.NewGuid().ToString("N")
        };

        using var doc = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens", payload, headers, cancellationToken);
        var root = doc.RootElement;

        var result = new VaultedCard
        {
            VaultId = root.GetProperty("id").GetString()!
        };
        if (root.TryGetProperty("customer", out var customer) && customer.TryGetProperty("id", out var cid))
        {
            result.CustomerId = cid.GetString();
        }
        if (root.TryGetProperty("payment_source", out var ps) && ps.TryGetProperty("card", out var respCard))
        {
            result.Last4 = respCard.TryGetProperty("last_digits", out var l4) ? l4.GetString() : null;
            result.Brand = respCard.TryGetProperty("brand", out var br) ? br.GetString() : null;
            result.Expiry = respCard.TryGetProperty("expiry", out var ex) ? ex.GetString() : null;
        }
        return result;
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken);
    }

    // ---------------------------------------------------------------------------------------------
    // Transaction Search for reconciliation. The reporting API caps each request at a ~31-day window
    // and pages results, so we chunk the requested range and page every window to cover it fully.
    // ---------------------------------------------------------------------------------------------
    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();
        const int windowDays = 31;
        const int pageSize = 500;

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(windowDays);
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            var totalPages = 1;
            do
            {
                var query = $"/v1/reporting/transactions" +
                            $"?start_date={Uri.EscapeDataString(FormatRfc3339(windowStart))}" +
                            $"&end_date={Uri.EscapeDataString(FormatRfc3339(windowEnd))}" +
                            $"&fields=all&page_size={pageSize}&page={page}";

                using var doc = await SendAsync(HttpMethod.Get, query, null, null, cancellationToken);
                var root = doc.RootElement;

                if (root.TryGetProperty("total_pages", out var tp) && tp.ValueKind == JsonValueKind.Number)
                {
                    totalPages = tp.GetInt32();
                }

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        if (!detail.TryGetProperty("transaction_info", out var info)) continue;
                        results.Add(MapTransaction(info));
                    }
                }

                page++;
            }
            while (page <= totalPages && !cancellationToken.IsCancellationRequested);

            windowStart = windowEnd;
        }

        return results;
    }

    private static PayPalTransaction MapTransaction(JsonElement info)
    {
        var tx = new PayPalTransaction
        {
            TransactionId = info.TryGetProperty("transaction_id", out var id) ? id.GetString() ?? "" : "",
            InvoiceId = info.TryGetProperty("invoice_id", out var inv) ? inv.GetString() : null,
            CustomField = info.TryGetProperty("custom_field", out var cf) ? cf.GetString() : null,
            Status = info.TryGetProperty("transaction_status", out var st) ? st.GetString() : null,
            InitiationDate = ReadDate(info, "transaction_initiation_date")
        };
        if (info.TryGetProperty("transaction_amount", out var amt))
        {
            if (amt.TryGetProperty("value", out var v) && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                tx.Amount = value;
            }
            tx.Currency = amt.TryGetProperty("currency_code", out var c) ? c.GetString() : null;
        }
        return tx;
    }

    // ---------------------------------------------------------------------------------------------
    // Shared request plumbing.
    // ---------------------------------------------------------------------------------------------
    private async Task<JsonDocument> SendAsync(HttpMethod method, string pathAndQuery, object? payload,
        IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, $"{_baseUrl}{pathAndQuery}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        if (payload is not null)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await BuildApiExceptionAsync($"PayPal request to {method} {pathAndQuery} failed", response, body);
        }

        // 204 No Content (e.g. void, delete) — return an empty document to keep call sites uniform.
        return string.IsNullOrWhiteSpace(body)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(body);
    }

    private Task<PayPalApiException> BuildApiExceptionAsync(string context, HttpResponseMessage response, string body)
    {
        string? debugId = null;
        string? issue = null;
        string message = context;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("debug_id", out var d)) debugId = d.GetString();
            if (root.TryGetProperty("message", out var m)) message = $"{context}: {m.GetString()}";
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
                && details.GetArrayLength() > 0)
            {
                var first = details[0];
                if (first.TryGetProperty("issue", out var iss)) issue = iss.GetString();
                if (first.TryGetProperty("description", out var desc)) message = $"{message} ({desc.GetString()})";
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the context message.
        }

        // Log the debug id and issue for traceability — never the request body / card data.
        _logger.LogWarning("PayPal API error: {Context} status={Status} issue={Issue} debug_id={DebugId}",
            context, (int)response.StatusCode, issue, debugId);

        return Task.FromResult(new PayPalApiException(message, (int)response.StatusCode, debugId, issue));
    }

    private static void PopulateCard(IDictionary<string, object?> card, CardDetails details)
    {
        card["number"] = details.Number;
        card["expiry"] = details.Expiry;
        if (!string.IsNullOrWhiteSpace(details.SecurityCode)) card["security_code"] = details.SecurityCode;
        if (!string.IsNullOrWhiteSpace(details.Name)) card["name"] = details.Name;

        if (details.BillingAddress is not null)
        {
            var addr = new Dictionary<string, object?> { ["country_code"] = details.BillingAddress.CountryCode };
            if (!string.IsNullOrWhiteSpace(details.BillingAddress.AddressLine1)) addr["address_line_1"] = details.BillingAddress.AddressLine1;
            if (!string.IsNullOrWhiteSpace(details.BillingAddress.AddressLine2)) addr["address_line_2"] = details.BillingAddress.AddressLine2;
            if (!string.IsNullOrWhiteSpace(details.BillingAddress.AdminArea1)) addr["admin_area_1"] = details.BillingAddress.AdminArea1;
            if (!string.IsNullOrWhiteSpace(details.BillingAddress.AdminArea2)) addr["admin_area_2"] = details.BillingAddress.AdminArea2;
            if (!string.IsNullOrWhiteSpace(details.BillingAddress.PostalCode)) addr["postal_code"] = details.BillingAddress.PostalCode;
            card["billing_address"] = addr;
        }
    }

    private static bool TryExtractAuthorization(JsonElement order, out JsonElement authorization)
    {
        authorization = default;
        if (!order.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("authorizations", out var auths)
                && auths.ValueKind == JsonValueKind.Array
                && auths.GetArrayLength() > 0)
            {
                authorization = auths[0];
                return true;
            }
        }
        return false;
    }

    private static bool TryReadMoney(JsonElement parent, string property, out decimal value)
    {
        value = 0m;
        if (parent.TryGetProperty(property, out var money)
            && money.TryGetProperty("value", out var v)
            && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        return false;
    }

    private static DateTimeOffset? ReadDate(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var d)
            && DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            return value;
        }
        return null;
    }

    private string FormatAmount(decimal amount)
    {
        // Format to the currency's minor-unit precision so the amount matches the order total to the cent.
        var decimals = ZeroDecimalCurrencies.Contains(Currency.ToUpperInvariant()) ? 0 : 2;
        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "VND", "CLP", "HUF", "TWD"
    };

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
