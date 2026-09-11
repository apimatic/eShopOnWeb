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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST gateway built strictly against the OpenAPI specs in <c>api-specs/paypal</c>:
/// Checkout Orders v2 (authorize), Payments v2 (capture/void/reauthorize/refund), Vault Payment
/// Tokens v3 (saved cards), and Transaction Search v1 (reconciliation).
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private const string TokenCacheKey = "paypal:access_token";
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int SearchPageSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<PayPalGateway> _logger;
    private static readonly SemaphoreSlim TokenLock = new(1, 1);

    public PayPalGateway(HttpClient http, IOptions<PayPalSettings> settings, IMemoryCache cache, IAppLogger<PayPalGateway> logger)
    {
        _settings = settings.Value;
        _http = http;
        _http.BaseAddress = new Uri(_settings.ResolveBaseUrl() + "/");
        _cache = cache;
        _logger = logger;
    }

    public string CurrencyCode => _settings.Currency;

    // ---------------------------------------------------------------- Authorize (Checkout Orders v2)

    public async Task<PayPalAuthorization> AuthorizeOrderAsync(AuthorizeOrderInput input, string idempotencyKey, CancellationToken ct)
    {
        var card = input.VaultId is not null
            ? new CardRequestDto { VaultId = input.VaultId }
            : new CardRequestDto
            {
                Number = input.Card!.Number,
                Expiry = input.Card.ExpiryMonthYear,
                SecurityCode = input.Card.SecurityCode,
                Name = input.Card.Name,
                BillingAddress = ToAddressDto(input.Card.BillingAddress)
            };

        var body = new OrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new()
            {
                new PurchaseUnitRequestDto
                {
                    ReferenceId = input.ReferenceId,
                    InvoiceId = input.InvoiceId,
                    CustomId = input.CustomId,
                    Amount = new MoneyDto { CurrencyCode = input.CurrencyCode, Value = FormatAmount(input.Amount) }
                }
            },
            PaymentSource = new PaymentSourceRequestDto { Card = card }
        };

        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };

        var order = await SendAsync<OrderResponseDto>(HttpMethod.Post, "v2/checkout/orders", body, headers, ct);

        // A card that triggers 3-D Secure comes back needing a browser approval step, which this
        // headless integration does not implement. Surface it clearly rather than faking a round-trip.
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            HasLink(order.Links, "payer-action"))
        {
            throw new PayPalApiException(422, "PAYER_ACTION_REQUIRED",
                "The card requires additional buyer authentication (3-D Secure), which needs a browser approval step.",
                null, new[] { "PAYER_ACTION_REQUIRED" });
        }

        var auth = FindAuthorization(order);
        if (auth?.Id is null)
            throw new PayPalApiException(502, "NO_AUTHORIZATION",
                $"PayPal did not return an authorization for order {order.Id} (status {order.Status}).", null, Array.Empty<string>());

        return MapAuthorization(order.Id!, auth);
    }

    public async Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        var auth = await SendAsync<AuthorizationDto>(HttpMethod.Get, $"v2/payments/authorizations/{authorizationId}", null, null, ct);
        return MapAuthorization(string.Empty, auth);
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, CancellationToken ct)
    {
        var body = new ReauthorizeRequestDto { Amount = new MoneyDto { CurrencyCode = currencyCode, Value = FormatAmount(amount) } };
        var headers = new Dictionary<string, string> { ["Prefer"] = "return=representation" };
        var auth = await SendAsync<AuthorizationDto>(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize", body, headers, ct);
        return MapAuthorization(string.Empty, auth);
    }

    // ---------------------------------------------------------------- Capture / Void / Refund (Payments v2)

    public async Task<PayPalCapture> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        var body = new CaptureRequestDto { FinalCapture = true };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };
        var capture = await SendAsync<CaptureDto>(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture", body, headers, ct);

        var srb = capture.SellerReceivableBreakdown;
        var gross = ParseAmount(srb?.GrossAmount) ?? ParseAmount(capture.Amount) ?? 0m;
        var fee = ParseAmount(srb?.PayPalFee) ?? 0m;
        var net = ParseAmount(srb?.NetAmount) ?? (gross - fee);
        var currency = srb?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode ?? _settings.Currency;

        return new PayPalCapture(capture.Id ?? string.Empty, capture.Status ?? "COMPLETED", gross, fee, net, currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        var headers = new Dictionary<string, string> { ["PayPal-Request-Id"] = idempotencyKey };
        await SendAsync<object>(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", null, headers, ct);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct)
    {
        var body = new RefundRequestDto
        {
            Amount = amount.HasValue ? new MoneyDto { CurrencyCode = currencyCode, Value = FormatAmount(amount.Value) } : null
        };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = idempotencyKey,
            ["Prefer"] = "return=representation"
        };
        var refund = await SendAsync<RefundDto>(HttpMethod.Post, $"v2/payments/captures/{captureId}/refund", body, headers, ct);
        return new PayPalRefundResult(
            refund.Id ?? string.Empty,
            refund.Status ?? "PENDING",
            ParseAmount(refund.Amount) ?? amount ?? 0m,
            refund.Amount?.CurrencyCode ?? currencyCode);
    }

    // ---------------------------------------------------------------- Vault (Payment Tokens v3)

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string? customerId, string idempotencyKey, CancellationToken ct)
    {
        var body = new VaultTokenRequestDto
        {
            PaymentSource = new VaultPaymentSourceDto
            {
                Card = new CardRequestDto
                {
                    Number = card.Number,
                    Expiry = card.ExpiryMonthYear,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = ToAddressDto(card.BillingAddress)
                }
            },
            Customer = customerId is not null ? new CustomerRefDto { Id = customerId } : null
        };
        var headers = new Dictionary<string, string> { ["PayPal-Request-Id"] = idempotencyKey };

        var token = await SendAsync<VaultTokenResponseDto>(HttpMethod.Post, "v3/vault/payment-tokens", body, headers, ct);
        if (token.Id is null)
            throw new PayPalApiException(502, "NO_VAULT_TOKEN", "PayPal did not return a vault token id.", null, Array.Empty<string>());

        var c = token.PaymentSource?.Card;
        return new VaultedCard(
            token.Id,
            token.Customer?.Id ?? customerId,
            c?.Brand ?? "UNKNOWN",
            c?.LastDigits ?? "****",
            c?.Expiry ?? card.ExpiryMonthYear,
            c?.Name ?? card.Name);
    }

    public async Task DeleteVaultedCardAsync(string tokenId, CancellationToken ct)
    {
        await SendAsync<object>(HttpMethod.Delete, $"v3/vault/payment-tokens/{tokenId}", null, null, ct);
    }

    // ---------------------------------------------------------------- Reconciliation (Transaction Search v1)

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<PayPalTransaction>();

        // PayPal caps a single query at a 31-day window, so walk the range in chunks...
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to) windowEnd = to;

            // ...and page through every page of each window so nothing is missed.
            var page = 1;
            int totalPages;
            do
            {
                var query = $"v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatDate(windowStart))}" +
                            $"&end_date={Uri.EscapeDataString(FormatDate(windowEnd))}" +
                            $"&fields=transaction_info&page_size={SearchPageSize}&page={page}";

                var response = await SendAsync<SearchResponseDto>(HttpMethod.Get, query, null, null, ct);
                totalPages = response.TotalPages;

                foreach (var detail in response.TransactionDetails ?? new())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null) continue;
                    results.Add(new PayPalTransaction(
                        info.TransactionId,
                        string.IsNullOrEmpty(info.InvoiceId) ? null : info.InvoiceId,
                        string.IsNullOrEmpty(info.CustomField) ? null : info.CustomField,
                        ParseAmount(info.TransactionAmount) ?? 0m,
                        info.TransactionAmount?.CurrencyCode ?? _settings.Currency,
                        info.TransactionStatus ?? string.Empty,
                        info.TransactionEventCode ?? string.Empty,
                        ParseDate(info.TransactionInitiationDate) ?? windowStart,
                        ParseAmount(info.FeeAmount)));
                }
                page++;
            } while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results;
    }

    // ---------------------------------------------------------------- HTTP / auth plumbing

    private async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string path, object? body, IDictionary<string, string>? headers, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (headers is not null)
            foreach (var kv in headers)
                request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw ToApiException(response.StatusCode, payload);

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(payload) || typeof(TResponse) == typeof(object))
            return default!;

        return JsonSerializer.Deserialize<TResponse>(payload, JsonOptions)
               ?? throw new PayPalApiException((int)response.StatusCode, "EMPTY_RESPONSE", "PayPal returned an empty response body.", null, Array.Empty<string>());
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && cached is not null)
            return cached;

        await TokenLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(TokenCacheKey, out cached) && cached is not null)
                return cached;

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

            using var response = await _http.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw ToApiException(response.StatusCode, payload);

            var token = JsonSerializer.Deserialize<TokenResponseDto>(payload, JsonOptions);
            if (token?.AccessToken is null)
                throw new PayPalApiException((int)response.StatusCode, "AUTH_FAILED", "PayPal did not return an access token.", null, Array.Empty<string>());

            var ttl = TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 60));
            _cache.Set(TokenCacheKey, token.AccessToken, ttl);
            return token.AccessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private PayPalApiException ToApiException(HttpStatusCode status, string payload)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ErrorResponseDto>(payload, JsonOptions);
            if (error?.Name is not null || error?.Message is not null)
            {
                var issues = new List<string>();
                if (error.Details is not null)
                    foreach (var d in error.Details)
                        if (!string.IsNullOrEmpty(d.Issue)) issues.Add(d.Issue!);

                var message = error.Message ?? error.Name ?? "PayPal request failed.";
                if (issues.Count > 0) message += $" ({string.Join("; ", issues)})";

                _logger.LogWarning($"PayPal API error {(int)status} {error.Name}: {error.Message} debug_id={error.DebugId}");
                return new PayPalApiException((int)status, error.Name ?? "PAYPAL_ERROR", message, error.DebugId, issues);
            }
        }
        catch (JsonException) { /* fall through to raw payload */ }

        _logger.LogWarning($"PayPal API error {(int)status}: {payload}");
        return new PayPalApiException((int)status, "PAYPAL_ERROR",
            $"PayPal request failed with status {(int)status}.", null, Array.Empty<string>());
    }

    // ---------------------------------------------------------------- mapping helpers

    private static CardAddressDto? ToAddressDto(PayPalAddress? a) => a is null ? null : new CardAddressDto
    {
        AddressLine1 = a.AddressLine1,
        AddressLine2 = a.AddressLine2,
        AdminArea2 = a.AdminArea2,
        AdminArea1 = a.AdminArea1,
        PostalCode = a.PostalCode,
        CountryCode = a.CountryCode
    };

    private PayPalAuthorization MapAuthorization(string orderId, AuthorizationDto auth) => new(
        orderId,
        auth.Id ?? string.Empty,
        auth.Status ?? "CREATED",
        ParseDate(auth.ExpirationTime),
        ParseAmount(auth.Amount) ?? 0m,
        auth.Amount?.CurrencyCode ?? _settings.Currency);

    private static AuthorizationDto? FindAuthorization(OrderResponseDto order)
    {
        if (order.PurchaseUnits is null) return null;
        foreach (var pu in order.PurchaseUnits)
        {
            var auths = pu.Payments?.Authorizations;
            if (auths is not null && auths.Count > 0) return auths[auths.Count - 1];
        }
        return null;
    }

    private static bool HasLink(List<LinkDto>? links, string rel) =>
        links is not null && links.Exists(l => string.Equals(l.Rel, rel, StringComparison.OrdinalIgnoreCase));

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(MoneyDto? money) =>
        money?.Value is { Length: > 0 } v && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        !string.IsNullOrEmpty(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
