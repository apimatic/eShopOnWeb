using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.Infrastructure.PayPal.Wire;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalClient : IPayPalClient
{
    private const string AccessTokenCacheKey = "PayPal:AccessToken";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // PayPal treats an explicit JSON null differently from an absent field for some
    // discriminated fields (e.g. payment_source.card.vault_id alongside card.number) - always
    // omit nulls on outgoing requests rather than serializing them.
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly IMemoryCache _cache;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(decimal amount, string currency, PayPalPaymentSource paymentSource, string idempotencyKey, CancellationToken ct = default)
    {
        var createRequest = new CreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new() { Amount = new Money(currency, FormatAmount(amount)) }
            }
        };

        var order = await SendAsync<OrderResponse>(HttpMethod.Post, "/v2/checkout/orders", createRequest, $"{idempotencyKey}-create", ct);

        var authorizeRequest = new AuthorizeOrderRequest
        {
            PaymentSource = new PaymentSourceRequest
            {
                Card = paymentSource.Card != null
                    ? new CardRequest
                    {
                        Name = paymentSource.Card.CardholderName,
                        Number = paymentSource.Card.Number,
                        Expiry = paymentSource.Card.Expiry,
                        SecurityCode = paymentSource.Card.SecurityCode,
                        BillingAddress = ToAddressWire(paymentSource.Card.BillingAddress)
                    }
                    : new CardRequest { VaultId = paymentSource.VaultId }
            }
        };

        var authorized = await SendAsync<OrderResponse>(HttpMethod.Post, $"/v2/checkout/orders/{order.Id}/authorize", authorizeRequest, $"{idempotencyKey}-authorize", ct);

        if (string.Equals(authorized.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            var payerActionLink = authorized.Links?.FirstOrDefault(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase))?.Href;
            throw new PayPalApprovalRequiredException(
                $"PayPal order {authorized.Id} requires buyer approval in a browser (payer-action: {payerActionLink ?? "n/a"}). " +
                "This integration only supports direct card authorization without a buyer redirect.");
        }

        var authorization = authorized.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization == null)
        {
            throw new PayPalApiException(System.Net.HttpStatusCode.UnprocessableEntity, "NO_AUTHORIZATION",
                $"PayPal order {authorized.Id} did not return an authorization (order status: {authorized.Status}).", null);
        }

        return new PayPalAuthorizationResult(authorized.Id, authorization.Id, authorization.Status, ParseDate(authorization.ExpirationTime));
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        var response = await SendAsync<AuthorizationResponse>(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, ct);
        return new PayPalAuthorizationResult(string.Empty, response.Id, response.Status, ParseDate(response.ExpirationTime));
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        var request = new ReauthorizeRequest { Amount = new Money(currency, FormatAmount(amount)) };
        var response = await SendAsync<AuthorizationResponse>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", request, idempotencyKey, ct);
        return new PayPalAuthorizationResult(string.Empty, response.Id, response.Status, ParseDate(response.ExpirationTime));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        await SendNoContentAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, null, ct);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        var response = await SendAsync<CaptureResponse>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", new { }, idempotencyKey, ct);
        var amount = ParseAmount(response.Amount);
        var fee = response.SellerReceivableBreakdown?.PayPalFee != null ? ParseAmount(response.SellerReceivableBreakdown.PayPalFee) : (decimal?)null;
        var net = response.SellerReceivableBreakdown?.NetAmount != null ? ParseAmount(response.SellerReceivableBreakdown.NetAmount) : (decimal?)null;
        return new PayPalCaptureResult(response.Id, response.Status, amount, fee, net);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        var request = new RefundRequest
        {
            Amount = amount.HasValue ? new Money(currency, FormatAmount(amount.Value)) : null
        };
        var response = await SendAsync<RefundResponse>(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", request, idempotencyKey, ct);
        return new PayPalRefundResult(response.Id, response.Status, ParseAmount(response.Amount));
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(PayPalCardDetails card, string customerId, string idempotencyKey, CancellationToken ct = default)
    {
        var request = new CreateVaultPaymentTokenRequest
        {
            PaymentSource = new VaultPaymentSourceRequest
            {
                Card = new VaultCardRequest
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToAddressWire(card.BillingAddress)
                }
            },
            Customer = new VaultCustomerRequest { Id = customerId }
        };

        var response = await SendAsync<PaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens", request, idempotencyKey, ct);
        var responseCard = response.PaymentSource?.Card;
        if (responseCard == null)
        {
            throw new PayPalApiException(System.Net.HttpStatusCode.UnprocessableEntity, "NO_CARD", "PayPal did not return card details for the vaulted payment method.", null);
        }

        return new PayPalVaultedCard(response.Id, responseCard.Brand ?? "UNKNOWN", responseCard.LastDigits ?? "????", responseCard.Expiry ?? card.Expiry);
    }

    public async Task DeleteVaultedPaymentTokenAsync(string paymentTokenId, CancellationToken ct = default)
    {
        await SendNoContentAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{paymentTokenId}", null, null, ct);
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransactionRecord>();

        foreach (var (windowStart, windowEnd) in ChunkByMaxWindow(from, to, TimeSpan.FromDays(31)))
        {
            var page = 1;
            while (true)
            {
                var query = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(FormatRfc3339(windowStart))}" +
                    $"&end_date={Uri.EscapeDataString(FormatRfc3339(windowEnd))}" +
                    $"&fields=transaction_info&page_size=100&page={page}&total_required=true";

                var response = await SendAsync<TransactionSearchResponse>(HttpMethod.Get, query, null, null, ct);

                foreach (var detail in response.TransactionDetails ?? new List<TransactionDetail>())
                {
                    var info = detail.TransactionInfo;
                    if (info == null) continue;

                    results.Add(new PayPalTransactionRecord(
                        info.TransactionId,
                        info.TransactionAmount != null ? ParseAmount(info.TransactionAmount) : 0m,
                        info.TransactionAmount?.CurrencyCode ?? _options.Currency,
                        ParseDate(info.TransactionInitiationDate) ?? windowStart,
                        info.TransactionStatus ?? string.Empty,
                        info.TransactionEventCode ?? string.Empty));
                }

                if (page >= response.TotalPages || response.TransactionDetails == null || response.TransactionDetails.Count == 0)
                {
                    break;
                }
                page++;
            }
        }

        return results;
    }

    private static IEnumerable<(DateTimeOffset start, DateTimeOffset end)> ChunkByMaxWindow(DateTimeOffset from, DateTimeOffset to, TimeSpan maxWindow)
    {
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + maxWindow < to ? windowStart + maxWindow : to;
            yield return (windowStart, windowEnd);
            windowStart = windowEnd;
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativeUrl, object? body, string? idempotencyKey, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, relativeUrl, body, idempotencyKey, ct);
        var stream = await response.Content.ReadAsStreamAsync(ct);
        var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
        return result ?? throw new PayPalApiException(response.StatusCode, "EMPTY_RESPONSE", "PayPal returned an empty response body.", null);
    }

    private async Task SendNoContentAsync(HttpMethod method, string relativeUrl, object? body, string? idempotencyKey, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, relativeUrl, body, idempotencyKey, ct);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string relativeUrl, object? body, string? idempotencyKey, CancellationToken ct)
    {
        var accessToken = await GetAccessTokenAsync(ct);

        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.Add("PayPal-Request-Id", idempotencyKey);
        }
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: RequestJsonOptions);
        }

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            PayPalErrorResponse? error = null;
            try
            {
                error = JsonSerializer.Deserialize<PayPalErrorResponse>(errorBody, JsonOptions);
            }
            catch (JsonException)
            {
                // fall through with raw body
            }

            var issues = error?.Details != null && error.Details.Count > 0
                ? string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}"))
                : null;
            var message = error?.Message ?? errorBody;
            if (issues != null)
            {
                message = $"{message} ({issues})";
            }

            throw new PayPalApiException(response.StatusCode, error?.Name, message, error?.DebugId);
        }

        return response;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(AccessTokenCacheKey, out string? cached) && cached != null)
        {
            return cached;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
        };
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new PayPalApiException(response.StatusCode, "AUTHENTICATION_FAILURE", $"Failed to obtain a PayPal access token: {errorBody}", null);
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, ct);
        if (token == null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new PayPalApiException(response.StatusCode, "AUTHENTICATION_FAILURE", "PayPal token response did not include an access token.", null);
        }

        var cacheDuration = TimeSpan.FromSeconds(Math.Max(token.ExpiresIn - 60, 60));
        _cache.Set(AccessTokenCacheKey, token.AccessToken, cacheDuration);
        return token.AccessToken;
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(Money? money) => money == null ? 0m : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static string FormatRfc3339(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrEmpty(value) ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static AddressWire ToAddressWire(ApplicationCore.PayPal.PayPalAddress address) => new()
    {
        CountryCode = address.CountryCode,
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.City,
        AdminArea1 = address.State,
        PostalCode = address.PostalCode
    };
}
