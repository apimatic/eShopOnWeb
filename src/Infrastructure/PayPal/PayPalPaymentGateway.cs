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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal gateway implemented directly against PayPal's OpenAPI specification (Checkout Orders
/// v2, Payments v2, Vault v3, Transaction Search v1). No third-party PayPal SDK is used.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    // PayPal transaction search only accepts ranges up to 31 days per request.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const string TokenCacheKey = "paypal-access-token";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly PayPalOptions _options;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(HttpClient http, IMemoryCache cache, IOptions<PayPalOptions> options, IAppLogger<PayPalPaymentGateway> logger)
    {
        _http = http;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GatewayAuthorization> AuthorizeOrderAsync(AuthorizeOrderRequest request, CancellationToken cancellationToken = default)
    {
        var card = request.Card is null ? null : new CardRequest
        {
            Number = request.Card.Number,
            Expiry = request.Card.Expiry,
            SecurityCode = request.Card.SecurityCode,
            Name = request.Card.CardholderName,
            BillingAddress = ToBillingAddress(request.Card.BillingAddress),
            // Avoid forcing a 3-D Secure browser challenge for direct card processing.
            Attributes = new CardAttributes { Verification = new CardVerification { Method = "SCA_WHEN_REQUIRED" } }
        };

        if (card is null && !string.IsNullOrEmpty(request.VaultId))
        {
            card = new CardRequest { VaultId = request.VaultId };
        }

        var body = new CreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new()
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = request.ReferenceId,
                    InvoiceId = request.InvoiceId,
                    CustomId = request.CustomId,
                    Amount = new Money { CurrencyCode = request.CurrencyCode, Value = FormatAmount(request.Amount) }
                }
            },
            PaymentSource = new PaymentSourceRequest { Card = card }
        };

        var order = await SendAsync<OrderResponse>(HttpMethod.Post, "/v2/checkout/orders", body, request.IdempotencyKey, preferRepresentation: true, cancellationToken);
        GuardAgainstChallenge(order);

        var authorization = FindAuthorization(order);
        if (authorization is null)
        {
            // Card supplied inline should have processed; if only APPROVED, place the authorization explicitly.
            if (string.Equals(order.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(order.Id))
            {
                var authorized = await SendAsync<OrderResponse>(HttpMethod.Post, $"/v2/checkout/orders/{order.Id}/authorize", new { }, $"{request.IdempotencyKey}-auth", preferRepresentation: true, cancellationToken);
                GuardAgainstChallenge(authorized);
                order = authorized;
                authorization = FindAuthorization(order);
            }
        }

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PayPalGatewayException(502, "NO_AUTHORIZATION",
                $"PayPal did not return an authorization for the order (status: {order.Status}).", null, Array.Empty<string>(), 200);
        }

        var respCard = order.PaymentSource?.Card;
        return new GatewayAuthorization(
            order.Id!,
            authorization.Id!,
            authorization.Status ?? "CREATED",
            ParseDate(authorization.ExpirationTime),
            respCard?.Brand,
            respCard?.LastDigits);
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount) },
            FinalCapture = true
        };

        var capture = await SendAsync<CaptureResponse>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, preferRepresentation: true, cancellationToken);

        var breakdown = capture.SellerReceivableBreakdown;
        var gross = ParseAmount(breakdown?.GrossAmount) ?? ParseAmount(capture.Amount) ?? amount;
        return new GatewayCapture(
            capture.Id ?? string.Empty,
            capture.Status ?? "COMPLETED",
            gross,
            ParseAmount(breakdown?.PaypalFee),
            ParseAmount(breakdown?.NetAmount),
            capture.Amount?.CurrencyCode ?? currencyCode);
    }

    public async Task<GatewayAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount) } };
        var auth = await SendAsync<AuthorizationResponse>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, idempotencyKey, preferRepresentation: true, cancellationToken);
        return new GatewayAuthorizationInfo(auth.Id ?? authorizationId, auth.Status ?? "CREATED", ParseDate(auth.ExpirationTime));
    }

    public async Task<GatewayAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var auth = await SendAsync<AuthorizationResponse>(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, preferRepresentation: false, cancellationToken);
        return new GatewayAuthorizationInfo(auth.Id ?? authorizationId, auth.Status ?? "CREATED", ParseDate(auth.ExpirationTime));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, idempotencyKey, preferRepresentation: false, cancellationToken, allowEmpty: true);
    }

    public async Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new RefundRequest
        {
            Amount = amount is null ? null : new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount.Value) }
        };
        var refund = await SendAsync<RefundResponse>(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, idempotencyKey, preferRepresentation: true, cancellationToken);
        return new GatewayRefund(
            refund.Id ?? string.Empty,
            refund.Status ?? "PENDING",
            ParseAmount(refund.Amount) ?? amount ?? 0m,
            refund.Amount?.CurrencyCode ?? currencyCode);
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default)
    {
        var body = new PaymentTokenRequest
        {
            Customer = string.IsNullOrEmpty(request.ExistingCustomerId)
                ? new VaultCustomer { MerchantCustomerId = request.MerchantCustomerId }
                : new VaultCustomer { Id = request.ExistingCustomerId },
            PaymentSource = new VaultPaymentSource
            {
                Card = new VaultCard
                {
                    Number = request.Card.Number,
                    Expiry = request.Card.Expiry,
                    SecurityCode = request.Card.SecurityCode,
                    Name = request.Card.CardholderName,
                    BillingAddress = ToBillingAddress(request.Card.BillingAddress)
                }
            }
        };

        var token = await SendAsync<PaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens", body, Guid.NewGuid().ToString("N"), preferRepresentation: false, cancellationToken);
        var respCard = token.PaymentSource?.Card;
        return new GatewayVaultedCard(
            token.Id ?? string.Empty,
            token.Customer?.Id,
            respCard?.Brand ?? "UNKNOWN",
            respCard?.LastDigits ?? string.Empty,
            respCard?.Expiry ?? request.Card.Expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<object>(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, preferRepresentation: false, cancellationToken, allowEmpty: true);
        }
        catch (PayPalGatewayException ex) when (ex.UpstreamStatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Vault token {0} was already gone at PayPal; treating delete as complete.", vaultId);
        }
    }

    public async Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<ReconciliationTransaction>();

        // Walk the whole range in <=31-day windows, paginating each window across all pages.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var query = $"/v1/reporting/transactions?start_date={FormatSearchDate(windowStart)}&end_date={FormatSearchDate(windowEnd)}" +
                            $"&fields=transaction_info&page_size=500&page={page}";
                var response = await SendAsync<TransactionSearchResponse>(HttpMethod.Get, query, null, null, preferRepresentation: false, cancellationToken);
                totalPages = response.TotalPages;

                foreach (var detail in response.TransactionDetails ?? new())
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    results.Add(new ReconciliationTransaction(
                        info.TransactionId ?? string.Empty,
                        info.TransactionEventCode,
                        info.TransactionStatus ?? string.Empty,
                        ParseAmount(info.TransactionAmount) ?? 0m,
                        info.TransactionAmount?.CurrencyCode ?? string.Empty,
                        ParseAmount(info.FeeAmount),
                        ParseDate(info.TransactionInitiationDate),
                        info.InvoiceId,
                        info.CustomField));
                }

                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd == windowStart ? to : windowEnd;
        }

        return results;
    }

    // ---------- HTTP plumbing ----------

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey, bool preferRepresentation, CancellationToken cancellationToken, bool allowEmpty = false)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var httpRequest = new HttpRequestMessage(method, ResolveUri(path));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            httpRequest.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (preferRepresentation)
        {
            httpRequest.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), JsonOptions);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var httpResponse = await _http.SendAsync(httpRequest, cancellationToken);
        var payload = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            throw BuildGatewayException(httpResponse.StatusCode, payload);
        }

        if (allowEmpty || string.IsNullOrWhiteSpace(payload))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
            ?? throw new PayPalGatewayException(502, "EMPTY_RESPONSE", "PayPal returned an empty response body.", null, Array.Empty<string>(), (int)httpResponse.StatusCode);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string>(TokenCacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ResolveUri("/v1/oauth2/token"));
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildGatewayException(response.StatusCode, payload);
        }

        var token = JsonSerializer.Deserialize<TokenResponse>(payload, JsonOptions);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new PayPalGatewayException(502, "TOKEN_ERROR", "PayPal did not return an access token.", null, Array.Empty<string>(), (int)response.StatusCode);
        }

        var ttl = TimeSpan.FromSeconds(Math.Max(30, token.ExpiresIn - 60));
        _cache.Set(TokenCacheKey, token.AccessToken, ttl);
        return token.AccessToken!;
    }

    private Uri ResolveUri(string path) => new($"{_options.ResolveBaseUrl()}{path}");

    private PayPalGatewayException BuildGatewayException(HttpStatusCode statusCode, string payload)
    {
        string name = "PAYPAL_ERROR";
        string message = $"PayPal request failed ({(int)statusCode}).";
        string? debugId = null;
        var issues = new List<string>();

        try
        {
            var error = JsonSerializer.Deserialize<PayPalError>(payload, JsonOptions);
            if (error is not null)
            {
                name = error.Name ?? error.Error ?? name;
                message = error.Message ?? error.ErrorDescription ?? message;
                debugId = error.DebugId;
                if (error.Details is not null)
                {
                    foreach (var d in error.Details)
                    {
                        if (!string.IsNullOrEmpty(d.Issue)) issues.Add(d.Issue!);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message.
        }

        var upstream = (int)statusCode;
        // Card declines / unprocessable requests surface to the caller as 422; auth issues as 502.
        var apiStatus = upstream is 400 or 422 ? 422 : upstream == 401 ? 502 : 502;
        _logger.LogWarning("PayPal error {0} ({1}) name={2} issues={3} debugId={4}", upstream, message, name, string.Join(",", issues), debugId ?? "-");

        var detailed = $"PayPal {name}: {message}";
        if (issues.Count > 0) detailed += $" [{string.Join(", ", issues)}]";
        if (!string.IsNullOrEmpty(debugId)) detailed += $" (debug_id: {debugId})";
        return new PayPalGatewayException(apiStatus, name, detailed, debugId, issues, upstream);
    }

    private static void GuardAgainstChallenge(OrderResponse order)
    {
        var status = order.Status ?? string.Empty;
        var needsPayerAction = status.Equals("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase);
        if (!needsPayerAction && order.Links is not null)
        {
            foreach (var link in order.Links)
            {
                if (string.Equals(link.Rel, "payer-action", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(link.Rel, "approve", StringComparison.OrdinalIgnoreCase))
                {
                    needsPayerAction = true;
                    break;
                }
            }
        }

        if (needsPayerAction)
        {
            throw new PayPalChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure challenge). " +
                "This integration is built for direct card payments only; a browser approval round-trip is out of scope.");
        }
    }

    private static AuthorizationResponse? FindAuthorization(OrderResponse order)
    {
        if (order.PurchaseUnits is null) return null;
        foreach (var unit in order.PurchaseUnits)
        {
            var auths = unit.Payments?.Authorizations;
            if (auths is not null && auths.Count > 0)
            {
                return auths[0];
            }
        }
        return null;
    }

    private static BillingAddress? ToBillingAddress(PayPalBillingAddress? address)
    {
        if (address is null) return null;
        return new BillingAddress
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(Money? money)
    {
        if (money?.Value is null) return null;
        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
    }

    private static string FormatSearchDate(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
}
