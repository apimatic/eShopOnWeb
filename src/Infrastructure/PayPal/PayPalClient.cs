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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// A hand-written PayPal client built directly against the OpenAPI specifications in
/// api-specs/paypal (checkout orders v2, payments v2, vault payment tokens v3, transaction search
/// v1). The specs are the authoritative contract for every endpoint, parameter, request/response
/// shape, auth scheme and error model used here.
/// </summary>
public class PayPalClient : IPaymentGateway
{
    private readonly HttpClient _http;
    private readonly PayPalTokenProvider _tokenProvider;
    private readonly PayPalOptions _options;
    private readonly IAppLogger<PayPalClient> _logger;

    public PayPalClient(HttpClient http, PayPalTokenProvider tokenProvider, PayPalOptions options,
        IAppLogger<PayPalClient> logger)
    {
        _http = http;
        _tokenProvider = tokenProvider;
        _options = options;
        _logger = logger;
    }

    // ---- Flow 1: pay for an order -----------------------------------------------------------

    public Task<AuthorizeResult> AuthorizeWithCardAsync(Money amount, CardDetails card, string invoiceId,
        string customId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var source = new PaymentSourceRequest
        {
            Card = new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.Name,
                BillingAddress = ToAddressDto(card)
            }
        };
        return CreateOrderAndAuthorizeAsync(amount, source, invoiceId, customId, idempotencyKey, cancellationToken);
    }

    public Task<AuthorizeResult> AuthorizeWithVaultTokenAsync(Money amount, string vaultTokenId, string invoiceId,
        string customId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var source = new PaymentSourceRequest
        {
            Card = new CardRequest { VaultId = vaultTokenId }
        };
        return CreateOrderAndAuthorizeAsync(amount, source, invoiceId, customId, idempotencyKey, cancellationToken);
    }

    private async Task<AuthorizeResult> CreateOrderAndAuthorizeAsync(Money amount, PaymentSourceRequest source,
        string invoiceId, string customId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var request = new CreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    InvoiceId = invoiceId,
                    CustomId = customId,
                    Amount = ToMoneyDto(amount)
                }
            },
            PaymentSource = source
        };

        var order = await SendAsync<OrderResponse>(HttpMethod.Post, "/v2/checkout/orders", request,
            idempotencyKey, cancellationToken);

        var authorization = order.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            // No authorization was produced. If PayPal needs the shopper to approve in a browser
            // (e.g. a 3-D Secure step-up), we stop and report it rather than building an approval
            // round-trip.
            if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentChallengeRequiredException(
                    "PayPal requires the shopper to approve this card payment in a browser " +
                    "(order status PAYER_ACTION_REQUIRED). This headless integration does not perform " +
                    "a browser approval step.");
            }

            throw new PaymentException(
                $"PayPal did not return an authorization for the order (status {order.Status ?? "unknown"}).");
        }

        var card = order.PaymentSource?.Card;
        return new AuthorizeResult(
            order.Id ?? string.Empty,
            authorization.Id!,
            authorization.Status ?? "CREATED",
            ParseDate(authorization.ExpirationTime),
            card?.Brand,
            card?.LastDigits);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, Money amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new CaptureRequest { Amount = ToMoneyDto(amount), FinalCapture = true };

        CaptureResponse capture;
        try
        {
            capture = await SendAsync<CaptureResponse>(HttpMethod.Post,
                $"/v2/payments/authorizations/{authorizationId}/capture", request, idempotencyKey, cancellationToken);
        }
        catch (PayPalApiException ex) when (IsAuthorizationExpired(ex))
        {
            throw new AuthorizationExpiredException(ex.Message, ex);
        }

        var breakdown = capture.SellerReceivableBreakdown;
        return new CaptureResult(
            capture.Id ?? string.Empty,
            capture.Status ?? "COMPLETED",
            ToMoney(breakdown?.GrossAmount) ?? amount,
            ToMoney(breakdown?.PayPalFee),
            ToMoney(breakdown?.NetAmount));
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, Money amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new ReauthorizeRequest { Amount = ToMoneyDto(amount) };
        var auth = await SendAsync<AuthorizationResponse>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", request, idempotencyKey, cancellationToken);

        return new ReauthorizeResult(
            auth.Id ?? authorizationId,
            auth.Status ?? "CREATED",
            ParseDate(auth.ExpirationTime));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", null, null, cancellationToken, allowEmpty: true);
    }

    public async Task<RefundResult> RefundAsync(string captureId, Money? amount, string invoiceId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // amount omitted => PayPal refunds the full remaining captured amount.
        var request = amount is null ? new RefundRequest() : new RefundRequest { Amount = ToMoneyDto(amount) };

        var refund = await SendAsync<RefundResponse>(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", request, idempotencyKey, cancellationToken);

        return new RefundResult(
            refund.Id ?? string.Empty,
            refund.Status ?? "COMPLETED",
            ToMoney(refund.Amount) ?? amount ?? new Money(_options.Currency, 0m));
    }

    // ---- Flow 2: saved cards ----------------------------------------------------------------

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string paypalCustomerId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new VaultPaymentTokenRequest
        {
            PaymentSource = new VaultPaymentSource
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = ToAddressDto(card)
                }
            },
            Customer = string.IsNullOrEmpty(paypalCustomerId) ? null : new VaultCustomer { Id = paypalCustomerId }
        };

        var token = await SendAsync<VaultPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens",
            request, idempotencyKey, cancellationToken);

        if (string.IsNullOrEmpty(token.Id))
        {
            throw new PaymentException("PayPal did not return a vault token id for the saved card.");
        }

        var savedCard = token.PaymentSource?.Card;
        return new VaultCardResult(
            token.Id!,
            token.Customer?.Id,
            savedCard?.Brand ?? "UNKNOWN",
            savedCard?.LastDigits ?? "",
            savedCard?.Expiry ?? card.Expiry);
    }

    public async Task DeleteVaultTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<object>(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultTokenId}",
                null, null, cancellationToken, allowEmpty: true);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The token is already gone at PayPal — deletion is idempotent, so treat as success.
            _logger.LogWarning($"Vault token {vaultTokenId} was already absent at PayPal on delete.");
        }
    }

    // ---- reconciliation ---------------------------------------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();
        var start = Uri.EscapeDataString(FormatSearchDate(from));
        var end = Uri.EscapeDataString(FormatSearchDate(to));

        var page = 1;
        var totalPages = 1;
        do
        {
            var path = $"/v1/reporting/transactions?start_date={start}&end_date={end}" +
                       $"&fields=all&page_size=500&page={page}";
            var response = await SendAsync<TransactionSearchResponse>(HttpMethod.Get, path, null, null, cancellationToken);
            totalPages = response.TotalPages ?? 1;

            foreach (var detail in response.TransactionDetails ?? new List<TransactionDetail>())
            {
                var info = detail.TransactionInfo;
                if (info is null) continue;
                results.Add(new PayPalTransaction(
                    info.TransactionId ?? string.Empty,
                    info.InvoiceId,
                    info.CustomField,
                    info.TransactionStatus,
                    info.TransactionEventCode,
                    ToMoney(info.TransactionAmount),
                    ToMoney(info.FeeAmount),
                    ParseDate(info.TransactionInitiationDate)));
            }

            page++;
        }
        while (page <= totalPages);

        return results;
    }

    // ---- HTTP plumbing ----------------------------------------------------------------------

    private async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string path, object? body,
        string? idempotencyKey, CancellationToken cancellationToken, bool allowEmpty = false)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, _options.ResolvedBaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: PayPalHttp.Json);
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException(response.StatusCode, content);
        }

        if (allowEmpty || string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(content, PayPalHttp.Json)!;
        }
        catch (JsonException ex)
        {
            throw new PaymentException($"Could not parse PayPal response: {ex.Message}", ex);
        }
    }

    private PayPalApiException BuildApiException(HttpStatusCode statusCode, string content)
    {
        string? name = null;
        string message = $"HTTP {(int)statusCode}";
        string? debugId = null;
        var issues = new List<PayPalErrorIssue>();

        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                var error = JsonSerializer.Deserialize<PayPalErrorResponse>(content, PayPalHttp.Json);
                if (error is not null)
                {
                    name = error.Name;
                    message = error.Message ?? message;
                    debugId = error.DebugId;
                    if (error.Details is not null)
                    {
                        issues.AddRange(error.Details.Select(d => new PayPalErrorIssue(d.Issue, d.Description)));
                    }
                }
            }
            catch (JsonException)
            {
                message = content;
            }
        }

        _logger.LogWarning($"PayPal API error {(int)statusCode} {name}: {message} (debug_id {debugId}).");
        return new PayPalApiException(statusCode, name, message, issues, debugId);
    }

    private static bool IsAuthorizationExpired(PayPalApiException ex) =>
        ex.HasIssue("AUTHORIZATION_EXPIRED") || ex.HasIssue("PAYMENT_AUTHORIZATION_EXPIRED");

    // ---- mapping helpers --------------------------------------------------------------------

    private static AddressDto? ToAddressDto(CardDetails card)
    {
        if (card.AddressLine1 is null && card.PostalCode is null && card.CountryCode is null
            && card.AdminArea1 is null && card.AdminArea2 is null)
        {
            return null;
        }
        return new AddressDto
        {
            AddressLine1 = card.AddressLine1,
            AddressLine2 = card.AddressLine2,
            AdminArea2 = card.AdminArea2,
            AdminArea1 = card.AdminArea1,
            PostalCode = card.PostalCode,
            CountryCode = card.CountryCode
        };
    }

    private static MoneyDto ToMoneyDto(Money money) => new()
    {
        CurrencyCode = money.CurrencyCode,
        Value = MoneyFormatter.Format(money.Amount, money.CurrencyCode)
    };

    private static Money? ToMoney(MoneyDto? dto) =>
        dto is null ? null : new Money(dto.CurrencyCode, MoneyFormatter.Parse(dto.Value));

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : null;

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
