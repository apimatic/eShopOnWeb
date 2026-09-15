using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Hand-written PayPal REST client built to the OpenAPI specs in <c>api-specs/paypal</c>:
/// Checkout Orders v2 (hold), Payments v2 (capture / void / reauthorize / refund), Vault
/// Payment Tokens v3 (saved cards) and Transaction Search v1 (reconciliation). No third-party
/// PayPal SDK is used. Card details flow straight to PayPal and are never logged.
/// </summary>
public class PayPalClient : IPaymentGateway, ICardVault, ITransactionReporting
{
    public const string HttpClientName = "paypal";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // PayPal Transaction Search caps a single query window at 31 days.
    private static readonly TimeSpan MaxReportWindow = TimeSpan.FromDays(31);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalTokenProvider _tokenProvider;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(IHttpClientFactory httpClientFactory, PayPalTokenProvider tokenProvider,
        IOptions<PayPalSettings> settings, ILogger<PayPalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Payments (hold/capture)

    public async Task<AuthorizationResult> AuthorizeAsync(PaymentAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var order = new OrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = "eshop-order",
                    InvoiceId = request.Reference,
                    CustomId = request.Reference,
                    Description = "eShopOnWeb order",
                    Amount = new Money { CurrencyCode = request.Currency, Value = FormatAmount(request.Amount) }
                }
            },
            PaymentSource = new PaymentSourceRequest { Card = BuildCard(request) }
        };

        var created = await SendAsync<OrderResponse>(HttpMethod.Post, "v2/checkout/orders", order,
            idempotencyKey: request.IdempotencyKey, preferRepresentation: true, cancellationToken: cancellationToken);

        EnsureNoChallenge(created);

        var payPalOrderId = created.Id ?? throw new PaymentException("PayPal did not return an order id.");
        var authorization = ExtractAuthorization(created);
        var cardEcho = created.PaymentSource?.Card;

        if (authorization is null)
        {
            // Card supplied at create but the hold is not yet placed: authorize the order now.
            var authorized = await SendAsync<OrderResponse>(HttpMethod.Post,
                $"v2/checkout/orders/{payPalOrderId}/authorize", new { },
                idempotencyKey: request.IdempotencyKey + "-auth", preferRepresentation: true,
                cancellationToken: cancellationToken);

            EnsureNoChallenge(authorized);
            authorization = ExtractAuthorization(authorized);
            cardEcho ??= authorized.PaymentSource?.Card;
        }

        if (authorization?.Id is null)
        {
            throw new PaymentException("PayPal did not return an authorization for the order.");
        }
        if (authorization.Status is "DENIED" or "VOIDED")
        {
            throw new PaymentException($"The card authorization was {authorization.Status}.");
        }

        return new AuthorizationResult(
            payPalOrderId,
            authorization.Id,
            authorization.Status ?? "CREATED",
            ParseTime(authorization.ExpirationTime),
            cardEcho?.Brand,
            cardEcho?.LastDigits);
    }

    public async Task<AuthorizationInfo> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<AuthorizationResponse>(HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}", null, cancellationToken: cancellationToken);

        return new AuthorizationInfo(
            authorization.Id ?? authorizationId,
            authorization.Status ?? "UNKNOWN",
            ParseTime(authorization.ExpirationTime));
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
        };

        var authorization = await SendAsync<AuthorizationResponse>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize", body,
            preferRepresentation: true, cancellationToken: cancellationToken);

        if (authorization.Id is null)
        {
            throw new PaymentException("PayPal did not return a renewed authorization.");
        }

        return new AuthorizationResult(string.Empty, authorization.Id, authorization.Status ?? "CREATED",
            ParseTime(authorization.ExpirationTime), null, null);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) },
            FinalCapture = true
        };

        var capture = await SendAsync<CaptureResponse>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture", body,
            idempotencyKey: idempotencyKey, preferRepresentation: true, cancellationToken: cancellationToken);

        if (capture.Id is null)
        {
            throw new PaymentException("PayPal did not return a capture.");
        }

        var breakdown = capture.SellerReceivableBreakdown;
        return new CaptureResult(
            capture.Id,
            capture.Status ?? "UNKNOWN",
            ParseAmount(capture.Amount) ?? amount,
            ParseAmount(breakdown?.PaypalFee),
            ParseAmount(breakdown?.NetAmount));
    }

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void", null,
            cancellationToken: cancellationToken);
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string? invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new RefundRequest
        {
            Amount = amount is null ? null : new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) },
            InvoiceId = invoiceId
        };

        var refund = await SendAsync<RefundResponse>(HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund", body,
            idempotencyKey: idempotencyKey, preferRepresentation: true, cancellationToken: cancellationToken);

        if (refund.Id is null)
        {
            throw new PaymentException("PayPal did not return a refund.");
        }

        return new RefundResult(refund.Id, refund.Status ?? "UNKNOWN",
            ParseAmount(refund.Amount) ?? amount ?? 0m);
    }

    // ---------------------------------------------------------------- Vault (saved cards)

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new PaymentTokenRequest
        {
            PaymentSource = new VaultPaymentSource
            {
                Card = new CardRequest
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = MapBillingAddress(card.BillingAddress)
                }
            }
        };

        var token = await SendAsync<PaymentTokenResponse>(HttpMethod.Post, "v3/vault/payment-tokens", body,
            idempotencyKey: idempotencyKey, cancellationToken: cancellationToken);

        if (token.Id is null)
        {
            throw new PaymentException("PayPal did not return a vault token for the card.");
        }

        var echo = token.PaymentSource?.Card;
        return new VaultedCard(
            token.Id,
            echo?.Brand ?? "UNKNOWN",
            echo?.LastDigits ?? "****",
            echo?.Expiry ?? card.Expiry,
            echo?.Type);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultId}", null,
            cancellationToken: cancellationToken);
    }

    // ---------------------------------------------------------------- Reporting (reconciliation)

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();
        var seen = new HashSet<string>();

        // Cover the whole range by chunking into PayPal's 31-day maximum window, paging each window.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxReportWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            int totalPages;
            do
            {
                var url = "v1/reporting/transactions" +
                          $"?start_date={Uri.EscapeDataString(FormatReportDate(windowStart))}" +
                          $"&end_date={Uri.EscapeDataString(FormatReportDate(windowEnd))}" +
                          "&fields=all&balance_affecting_records_only=N&page_size=500" +
                          $"&page={page}";

                TransactionSearchResponse response;
                try
                {
                    response = await SendAsync<TransactionSearchResponse>(HttpMethod.Get, url, null,
                        cancellationToken: cancellationToken);
                }
                catch (PayPalApiException ex) when (IsReportingDataNotYetAvailable(ex))
                {
                    // PayPal's reporting lags live activity; a recent window can legitimately have no
                    // data yet. That is an expected empty result, not a failure — skip this window.
                    _logger.LogInformation(
                        "PayPal reporting has no data yet for window {Start}..{End}; treating as empty.",
                        FormatReportDate(windowStart), FormatReportDate(windowEnd));
                    break;
                }

                foreach (var detail in response.TransactionDetails ?? new List<TransactionDetail>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    var key = $"{info.TransactionId}:{info.TransactionEventCode}";
                    if (!seen.Add(key))
                    {
                        continue; // de-dupe across overlapping window boundaries.
                    }

                    results.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.TransactionEventCode,
                        info.TransactionStatus ?? string.Empty,
                        ParseAmount(info.TransactionAmount) ?? 0m,
                        info.TransactionAmount?.CurrencyCode ?? _settings.Currency,
                        ParseAmount(info.FeeAmount),
                        info.InvoiceId,
                        info.CustomField,
                        ParseTime(info.TransactionInitiationDate)));
                }

                totalPages = response.TotalPages ?? 1;
                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        return results;
    }

    // ---------------------------------------------------------------- HTTP plumbing

    private static CardRequest BuildCard(PaymentAuthorizationRequest request)
    {
        if (!string.IsNullOrEmpty(request.VaultId))
        {
            // Pay with a saved (vaulted) card.
            return new CardRequest { VaultId = request.VaultId };
        }

        var card = request.Card ?? throw new PaymentException("No card or saved card supplied for the payment.");
        return new CardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = MapBillingAddress(card.BillingAddress)
        };
    }

    private static AddressPortable? MapBillingAddress(BillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }
        return new AddressPortable
        {
            AddressLine1 = address.Line1,
            AddressLine2 = address.Line2,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static AuthorizationResponse? ExtractAuthorization(OrderResponse order) =>
        order.PurchaseUnits?
            .FirstOrDefault(pu => pu.Payments?.Authorizations is { Count: > 0 })?
            .Payments?.Authorizations?.FirstOrDefault();

    private static void EnsureNoChallenge(OrderResponse order)
    {
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            (order.Links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (payer action / 3-D Secure challenge). " +
                "This integration does not build an approval round-trip.");
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey = null,
        bool preferRepresentation = false, CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(method, path, body, idempotencyKey, preferRepresentation, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        if (payload is null)
        {
            throw new PaymentException($"PayPal returned an empty body for {method} {path}.");
        }
        return payload;
    }

    private async Task SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        using var _ = await SendCoreAsync(method, path, body, idempotencyKey, false, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body,
        string? idempotencyKey, bool preferRepresentation, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
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
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        _logger.LogInformation("PayPal {Method} {Path}", method, path);

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowFromErrorResponseAsync(response, method, path, cancellationToken);
        }
        return response;
    }

    private async Task ThrowFromErrorResponseAsync(HttpResponseMessage response, HttpMethod method, string path,
        CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        PayPalErrorResponse? error = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                error = JsonSerializer.Deserialize<PayPalErrorResponse>(raw, JsonOptions);
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with the raw text.
        }

        var issues = new List<string>();
        if (error?.Name is not null)
        {
            issues.Add(error.Name);
        }
        if (error?.Details is not null)
        {
            issues.AddRange(error.Details.Where(d => d.Issue is not null).Select(d => d.Issue!));
        }

        var message = error?.Message
            ?? (string.IsNullOrWhiteSpace(raw) ? $"PayPal {method} {path} failed with {(int)response.StatusCode}." : raw);

        _logger.LogWarning("PayPal error on {Method} {Path}: {Status} {Name} {Issues}",
            method, path, (int)response.StatusCode, error?.Name, string.Join(",", issues));

        throw new PayPalApiException(response.StatusCode, error?.Name, message, error?.DebugId, issues);
    }

    private string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(Money? money)
    {
        if (money?.Value is null)
        {
            return null;
        }
        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static bool IsReportingDataNotYetAvailable(PayPalApiException ex) =>
        string.Equals(ex.PayPalName, "INVALID_REQUEST", StringComparison.OrdinalIgnoreCase) &&
        (ex.Message?.Contains("not available", StringComparison.OrdinalIgnoreCase) ?? false);
}
