using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;
using PayPal;
using PayPal.Core.ErrorResponse;
using PayPal.Core.Exceptions;
using PayPal.Errors;
using PayPal.Models;
using PayPal.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPalPayments;

public sealed class PayPalCheckoutGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(100);
    private const int MaxSearchPages = 100;
    private const int MaxSearchWindows = 48;

    private readonly PayPalClient _client;
    private readonly ILogger<PayPalCheckoutGateway> _logger;

    public PayPalCheckoutGateway(PayPalClient client, ILogger<PayPalCheckoutGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<AuthorizationResult> AuthorizeAsync(
        int orderId,
        string invoiceId,
        decimal amount,
        string currency,
        CardPaymentDetails? card,
        string? vaultId,
        CancellationToken cancellationToken) =>
        Bounded(ct => AuthorizeCoreAsync(orderId, invoiceId, amount, currency, card, vaultId, ct), cancellationToken);

    public Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            try
            {
                var auth = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: ct);
                return ToAuthorizationResult(auth, payPalOrderId: string.Empty);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                throw Map(ex.Error, ex);
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw WrapTransportOrParse(ex);
            }
        }, cancellationToken);

    public Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            try
            {
                var auth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money
                        {
                            CurrencyCode = currency,
                            Value = PayPalMoney.ToValue(amount)
                        }
                    },
                    prefer: "return=representation",
                    ct: ct);
                _logger.LogInformation("PayPal reauthorized {AuthorizationId} as {NewId}", authorizationId, auth.Id);
                return ToAuthorizationResult(auth, payPalOrderId: string.Empty);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw Map(ex.Error, ex);
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw WrapTransportOrParse(ex);
            }
        }, cancellationToken);

    public Task<CaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            try
            {
                var capture = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest
                    {
                        Amount = new Money
                        {
                            CurrencyCode = currency,
                            Value = PayPalMoney.ToValue(amount)
                        },
                        FinalCapture = true
                    },
                    prefer: "return=representation",
                    ct: ct);
                _logger.LogInformation("PayPal captured {CaptureId} for authorization {AuthorizationId}", capture.Id, authorizationId);
                return ToCaptureResult(capture);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw Map(ex.Error, ex);
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw WrapTransportOrParse(ex);
            }
        }, cancellationToken);

    public Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            try
            {
                var capture = await _client.Payments.GetCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    ct: ct);
                return ToCaptureResult(capture);
            }
            catch (SdkException<GetCapturedPaymentError> ex)
            {
                throw Map(ex.Error, ex);
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw WrapTransportOrParse(ex);
            }
        }, cancellationToken);

    public Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            try
            {
                var auth = await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: requestId,
                    prefer: "return=representation",
                    ct: ct);
                _logger.LogInformation("PayPal voided authorization {AuthorizationId}", authorizationId);
                return auth.Status?.Value ?? "VOIDED";
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw Map(ex.Error, ex);
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw WrapTransportOrParse(ex);
            }
        }, cancellationToken);

    public Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            try
            {
                RefundRequest? body = amount is decimal value
                    ? new RefundRequest
                    {
                        Amount = new Money
                        {
                            CurrencyCode = currency,
                            Value = PayPalMoney.ToValue(value)
                        }
                    }
                    : null;

                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: ct);
                _logger.LogInformation("PayPal refund {RefundId} for capture {CaptureId}", refund.Id, captureId);
                return new RefundResult(
                    refund.Id ?? throw new PaymentException("PayPal refund response omitted id."),
                    refund.Status?.Value ?? "COMPLETED",
                    PayPalMoney.FromValue(refund.Amount?.Value),
                    refund.Amount?.CurrencyCode ?? currency);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw Map(ex.Error, ex);
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw WrapTransportOrParse(ex);
            }
        }, cancellationToken);

    public Task<VaultedCardResult> VaultCardAsync(
        string merchantCustomerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            try
            {
                var response = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: Guid.NewGuid().ToString(),
                    body: new PaymentTokenRequest
                    {
                        Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                        PaymentSource = new PaymentTokenRequestPaymentSource
                        {
                            Card = ToVaultCard(card)
                        }
                    },
                    ct: ct);

                var vaultId = response.Id ?? throw new PaymentException("PayPal vault response omitted id.");
                var cardEntity = response.PaymentSource?.Card;
                _logger.LogInformation("PayPal vaulted payment token {VaultId}", vaultId);
                return new VaultedCardResult(
                    vaultId,
                    cardEntity?.LastDigits ?? LastDigits(card.Number),
                    cardEntity?.Brand?.Value ?? "CARD",
                    cardEntity?.Expiry ?? card.Expiry,
                    cardEntity?.Name ?? card.Name);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw Map(ex.Error, ex);
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw WrapTransportOrParse(ex);
            }
        }, cancellationToken);

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
                _logger.LogInformation("PayPal deleted payment token {VaultId}", vaultId);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw Map(ex.Error, ex);
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw WrapTransportOrParse(ex);
            }
        }, cancellationToken);

    public Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            var results = new List<ProviderTransaction>();
            var windows = 0;
            var cursor = from;
            while (true)
            {
                if (++windows > MaxSearchWindows)
                {
                    throw new PaymentException("Reconciliation range is too large.");
                }

                var windowEnd = cursor.AddDays(31);
                if (windowEnd > to)
                {
                    windowEnd = to;
                }

                await SearchWindowAsync(cursor, windowEnd, results, ct);
                if (windowEnd >= to)
                {
                    break;
                }

                cursor = windowEnd;
            }

            return (IReadOnlyList<ProviderTransaction>)results;
        }, cancellationToken);

    private async Task<AuthorizationResult> AuthorizeCoreAsync(
        int orderId,
        string invoiceId,
        decimal amount,
        string currency,
        CardPaymentDetails? card,
        string? vaultId,
        CancellationToken ct)
    {
        try
        {
            var body = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits =
                [
                    new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = currency,
                            Value = PayPalMoney.ToValue(amount)
                        },
                        CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                        InvoiceId = invoiceId
                    }
                ],
                PaymentSource = new PaymentSource
                {
                    Card = ToCardRequest(card, vaultId)
                }
            };

            var created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"pay-{invoiceId}",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            EnsureNoPayerAction(created.Status, created.Id, created.Links);

            var authorization = FirstAuthorization(created.PurchaseUnits);
            if (authorization is null)
            {
                var authorized = await _client.Orders.AuthorizeOrder(
                    id: created.Id ?? throw new PaymentException("PayPal create-order response omitted id."),
                    payPalMockResponse: null,
                    payPalRequestId: $"auth-{invoiceId}",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct);

                EnsureNoPayerAction(authorized.Status, authorized.Id, authorized.Links);
                authorization = FirstAuthorization(authorized.PurchaseUnits);
                if (authorization is null)
                {
                    throw new PaymentException("PayPal authorized the order but returned no authorization id.");
                }

                _logger.LogInformation(
                    "PayPal authorized eShop order {OrderId} as PayPal order {PayPalOrderId} authorization {AuthorizationId}",
                    orderId,
                    authorized.Id,
                    authorization.Id);

                return ToAuthorizationResult(authorization, authorized.Id ?? created.Id ?? string.Empty, currency);
            }

            _logger.LogInformation(
                "PayPal authorized eShop order {OrderId} as PayPal order {PayPalOrderId} authorization {AuthorizationId}",
                orderId,
                created.Id,
                authorization.Id);

            return ToAuthorizationResult(authorization, created.Id ?? string.Empty, currency);
        }
        catch (PayerActionRequiredException)
        {
            throw;
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw Map(ex.Error, ex);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw Map(ex.Error, ex);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw WrapTransportOrParse(ex);
        }
    }

    private async Task SearchWindowAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<ProviderTransaction> sink,
        CancellationToken ct)
    {
        var start = FormatReportingInstant(from);
        var end = FormatReportingInstant(to);
        var page = 1;
        int? totalPages = null;

        do
        {
            if (page > MaxSearchPages)
            {
                _logger.LogWarning("Stopped PayPal transaction search after {MaxPages} pages", MaxSearchPages);
                break;
            }

            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: start,
                    endDate: end,
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: "N",
                    pageSize: 100,
                    page: page,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw new PaymentException(
                    $"PayPal transaction search failed with HTTP {(int)ex.Error.StatusCode}.",
                    MapHttp(ex.Error.StatusCode),
                    ex);
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw WrapTransportOrParse(ex);
            }

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }

                    sink.Add(new ProviderTransaction(
                        info.TransactionId ?? string.Empty,
                        info.InvoiceId,
                        info.CustomField,
                        info.PaypalReferenceId,
                        info.TransactionStatus,
                        info.TransactionAmount?.Value,
                        info.FeeAmount?.Value,
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionInitiationDate));
                }
            }

            totalPages = response.TotalPages;
            page++;
        } while (totalPages is int pages && page <= pages);
    }

    private static void EnsureNoPayerAction(OrderStatus? status, string? orderId, IReadOnlyList<LinkDescription>? links)
    {
        if (status == OrderStatus.PayerActionRequired ||
            links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new PayerActionRequiredException(orderId ?? "unknown");
        }
    }

    private static CardRequest ToCardRequest(CardPaymentDetails? card, string? vaultId)
    {
        if (!string.IsNullOrEmpty(vaultId))
        {
            return new CardRequest { VaultId = vaultId };
        }

        if (card is null)
        {
            throw new InvalidOrderStateException("Card details are required when not paying with a saved card.");
        }

        return new CardRequest
        {
            Number = DigitsOnly(card.Number),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = ToPayPalAddress(card.BillingAddress)
        };
    }

    private static PaymentTokenRequestCard ToVaultCard(CardPaymentDetails card) =>
        new()
        {
            Number = DigitsOnly(card.Number),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = ToPayPalAddress(card.BillingAddress)
        };

    private static Address? ToPayPalAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new Address
        {
            CountryCode = address.CountryCode,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode
        };
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? units) =>
        units?.SelectMany(u => u.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault(a => a.Id is not null);

    private static AuthorizationResult ToAuthorizationResult(
        AuthorizationWithAdditionalData authorization,
        string payPalOrderId,
        string currency) =>
        new(
            payPalOrderId,
            authorization.Id ?? throw new PaymentException("PayPal authorization omitted id."),
            authorization.Status?.Value ?? "CREATED",
            ParseTime(authorization.ExpirationTime),
            authorization.Amount?.CurrencyCode ?? currency,
            authorization.Amount?.Value ?? string.Empty);

    private static AuthorizationResult ToAuthorizationResult(PaymentAuthorization authorization, string payPalOrderId) =>
        new(
            payPalOrderId,
            authorization.Id ?? throw new PaymentException("PayPal authorization omitted id."),
            authorization.Status?.Value ?? "CREATED",
            ParseTime(authorization.ExpirationTime),
            authorization.Amount?.CurrencyCode ?? string.Empty,
            authorization.Amount?.Value ?? string.Empty);

    private static CaptureResult ToCaptureResult(CapturedPayment capture) =>
        new(
            capture.Id ?? throw new PaymentException("PayPal capture omitted id."),
            capture.Status?.Value ?? "COMPLETED",
            PayPalMoney.FromValue(capture.Amount?.Value ?? capture.SellerReceivableBreakdown?.GrossAmount.Value),
            capture.SellerReceivableBreakdown?.PaypalFee is Money fee ? PayPalMoney.FromValue(fee.Value) : null,
            capture.SellerReceivableBreakdown?.NetAmount is Money net ? PayPalMoney.FromValue(net.Value) : null,
            capture.Amount?.CurrencyCode ?? capture.SellerReceivableBreakdown?.GrossAmount.CurrencyCode ?? string.Empty);

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string FormatReportingInstant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

    private static string LastDigits(string number)
    {
        var digits = DigitsOnly(number);
        return digits.Length <= 4 ? digits : digits[^4..];
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        await call(cts.Token);
    }

    private static bool IsTransportOrParse(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;

    private PaymentException WrapTransportOrParse(Exception ex)
    {
        if (ex is JsonException)
        {
            _logger.LogWarning(ex, "PayPal returned a body that could not be processed");
            return new PaymentException("The provider returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
        }

        _logger.LogWarning(ex, "PayPal was unreachable");
        return new PaymentException("PayPal was unreachable.", HttpStatusCode.BadGateway, ex);
    }

    private PaymentException Map(CreateOrderError error, Exception inner) => MapTyped(error, inner, e => e.TryGetError(out var body) ? body : null);
    private PaymentException Map(AuthorizeOrderError error, Exception inner) => MapTyped(error, inner, e => e.TryGetError(out var body) ? body : null);
    private PaymentException Map(GetAuthorizedPaymentError error, Exception inner) => MapTyped(error, inner, e => e.TryGetError(out var body) ? body : null, e => e.TryGetNoContent(out var raw) ? raw : null);
    private PaymentException Map(ReauthorizePaymentError error, Exception inner) => MapTyped(error, inner, e => e.TryGetError(out var body) ? body : null, e => e.TryGetNoContent(out var raw) ? raw : null);
    private PaymentException Map(CaptureAuthorizedPaymentError error, Exception inner) => MapTyped(error, inner, e => e.TryGetError(out var body) ? body : null, e => e.TryGetNoContent(out var raw) ? raw : null);
    private PaymentException Map(GetCapturedPaymentError error, Exception inner) => MapTyped(error, inner, e => e.TryGetError(out var body) ? body : null, e => e.TryGetNoContent(out var raw) ? raw : null);
    private PaymentException Map(VoidPaymentError error, Exception inner) => MapTyped(error, inner, e => e.TryGetError(out var body) ? body : null, e => e.TryGetNoContent(out var raw) ? raw : null);
    private PaymentException Map(RefundCapturedPaymentError error, Exception inner) => MapTyped(error, inner, e => e.TryGetError(out var body) ? body : null, e => e.TryGetNoContent(out var raw) ? raw : null);
    private PaymentException Map(CreatePaymentTokenError error, Exception inner) => MapTyped(error, inner, e => e.TryGetError(out var body) ? body : null);
    private PaymentException Map(DeletePaymentTokenError error, Exception inner) => MapTyped(error, inner, e => e.TryGetError(out var body) ? body : null);

    private PaymentException MapTyped<TError>(
        TError error,
        Exception inner,
        Func<TError, Error?> tryError,
        Func<TError, RawError?>? tryNoContent = null)
        where TError : ApiError
    {
        var typed = tryError(error);
        if (typed is not null)
        {
            _logger.LogWarning("PayPal error {Name} debug {DebugId}", typed.Name, typed.DebugId);
            return FromPayPalError(typed, inner);
        }

        var noContent = tryNoContent?.Invoke(error);
        if (noContent is not null)
        {
            return new PaymentException($"PayPal returned HTTP {(int)noContent.StatusCode}.", MapHttp(noContent.StatusCode), inner);
        }

        if (error.TryGetRawError(out var raw))
        {
            return new PaymentException($"PayPal returned HTTP {(int)raw.StatusCode}.", MapHttp(raw.StatusCode), inner);
        }

        return new PaymentException("PayPal returned an unrecognised error.", HttpStatusCode.BadGateway, inner);
    }

    private static PaymentException FromPayPalError(Error error, Exception inner)
    {
        var status = error.Name switch
        {
            "INVALID_REQUEST" => HttpStatusCode.BadRequest,
            "INVALID_PARAMETER_VALUE" => HttpStatusCode.BadRequest,
            "RESOURCE_NOT_FOUND" => HttpStatusCode.NotFound,
            "PERMISSION_DENIED" => HttpStatusCode.Forbidden,
            "UNPROCESSABLE_ENTITY" => HttpStatusCode.UnprocessableEntity,
            _ => HttpStatusCode.BadGateway
        };

        var detail = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(d => d.Description ?? d.Issue))
            : error.Message;

        return new PaymentException(detail, status, inner)
        {
            ProviderDebugId = error.DebugId,
            ProviderErrorName = error.Name
        };
    }

    private static HttpStatusCode MapHttp(HttpStatusCode status) =>
        (int)status is >= 400 and < 500 and not (401 or 403 or 429)
            ? status
            : HttpStatusCode.BadGateway;
}
