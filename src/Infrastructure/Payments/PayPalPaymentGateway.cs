using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPal;
using PayPal.Core;
using PayPal.Core.ErrorResponse;
using PayPal.Core.Exceptions;
using PayPal.Core.Hooks;
using PayPal.Errors;
using PayPal.Models;
using PayPal.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);
    private readonly PayPalClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalClient client, IOptions<PayPalSettings> settings,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
        Currency = settings.Value.Currency;
    }

    public string Currency { get; }

    public async Task<ProviderAuthorization> AuthorizeAsync(int orderId, string invoiceId, decimal amount, CardInput? card,
        string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        var amountText = MoneyText(amount);
        var order = await ExecuteTypedAsync<PayPal.Models.Order, CreateOrderError>(
            (options, ct) => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId + "-create",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits =
                    [
                        new PurchaseUnitRequest
                        {
                            Amount = new AmountWithBreakdown { CurrencyCode = Currency, Value = amountText },
                            InvoiceId = invoiceId,
                            CustomId = orderId.ToString(CultureInfo.InvariantCulture)
                        }
                    ]
                },
                prefer: "return=representation",
                requestOptions: options,
                ct: ct),
            error => error.TryGetError(out var value) ? value : null,
            RawFrom,
            isWrite: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(order.Id))
        {
            throw ProviderSchemaError("PayPal did not return an order id.");
        }

        var cardRequest = card is null
            ? new CardRequest { VaultId = vaultId }
            : ToCardRequest(card);

        var authorized = await ExecuteTypedAsync<OrderAuthorizeResponse, AuthorizeOrderError>(
            (options, ct) => _client.Orders.AuthorizeOrder(
                id: order.Id,
                payPalMockResponse: null,
                payPalRequestId: requestId + "-authorize",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = cardRequest }
                },
                prefer: "return=representation",
                requestOptions: options,
                ct: ct),
            error => error.TryGetError(out var value) ? value : null,
            RawFrom,
            isWrite: true,
            cancellationToken);

        if (authorized.Status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentException(PaymentFailureKind.PayerActionRequired,
                "PayPal requires browser approval for this card payment.");
        }

        var providerAuthorization = authorized.PurchaseUnits?
            .SelectMany(unit => unit.Payments?.Authorizations ?? [])
            .FirstOrDefault();

        if (providerAuthorization?.Id is null || providerAuthorization.Status is null ||
            providerAuthorization.Amount is null)
        {
            throw ProviderSchemaError("PayPal did not return an authorization for the order.");
        }

        return new ProviderAuthorization(
            authorized.Id ?? order.Id,
            providerAuthorization.Id,
            providerAuthorization.Status.Value,
            ParseMoney(providerAuthorization.Amount.Value),
            providerAuthorization.Amount.CurrencyCode,
            ParseDate(providerAuthorization.CreateTime),
            ParseDate(providerAuthorization.ExpirationTime));
    }

    public Task<ProviderAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken) =>
        GetAuthorizationCoreAsync(authorizationId, cancellationToken);

    private async Task<ProviderAuthorization> GetAuthorizationCoreAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        var authorization = await ExecuteTypedAsync<PaymentAuthorization, GetAuthorizedPaymentError>(
            (options, ct) => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: options,
                ct: ct),
            error => error.TryGetError(out var value) ? value : null,
            error => RawFrom(error, error.TryGetNoContent),
            isWrite: false,
            cancellationToken);

        return MapAuthorization(authorization, orderId: string.Empty);
    }

    public async Task<ProviderAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var authorization = await ExecuteTypedAsync<PaymentAuthorization, ReauthorizePaymentError>(
            (options, ct) => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = MoneyText(amount) }
                },
                prefer: "return=representation",
                requestOptions: options,
                ct: ct),
            error => error.TryGetError(out var value) ? value : null,
            error => RawFrom(error, error.TryGetNoContent),
            isWrite: true,
            cancellationToken);

        return MapAuthorization(authorization, orderId: string.Empty);
    }

    public async Task<ProviderCapture> CaptureAsync(string authorizationId, string invoiceId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var capture = await ExecuteTypedAsync<CapturedPayment, CaptureAuthorizedPaymentError>(
            (options, ct) => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: new CaptureRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = MoneyText(amount) },
                    InvoiceId = invoiceId,
                    FinalCapture = true
                },
                prefer: "return=representation",
                requestOptions: options,
                ct: ct),
            error => error.TryGetError(out var value) ? value : null,
            error => RawFrom(error, error.TryGetNoContent),
            isWrite: true,
            cancellationToken);

        return MapCapture(capture);
    }

    public async Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        var capture = await ExecuteTypedAsync<CapturedPayment, GetCapturedPaymentError>(
            (options, ct) => _client.Payments.GetCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                requestOptions: options,
                ct: ct),
            error => error.TryGetError(out var value) ? value : null,
            error => RawFrom(error, error.TryGetNoContent),
            isWrite: false,
            cancellationToken);

        return MapCapture(capture);
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        var authorization = await ExecuteTypedAsync<PaymentAuthorization, VoidPaymentError>(
            (options, ct) => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: requestId,
                prefer: "return=representation",
                requestOptions: options,
                ct: ct),
            error => error.TryGetError(out var value) ? value : null,
            error => RawFrom(error, error.TryGetNoContent),
            isWrite: true,
            cancellationToken);

        return authorization.Status?.Value ?? "VOIDED";
    }

    public async Task<ProviderRefund> RefundAsync(string captureId, string invoiceId, int orderId, decimal? amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var refund = await ExecuteTypedAsync<Refund, RefundCapturedPaymentError>(
            (options, ct) => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: new RefundRequest
                {
                    Amount = amount.HasValue
                        ? new Money { CurrencyCode = currency, Value = MoneyText(amount.Value) }
                        : null,
                    InvoiceId = invoiceId,
                    CustomId = orderId.ToString(CultureInfo.InvariantCulture)
                },
                prefer: "return=representation",
                requestOptions: options,
                ct: ct),
            error => error.TryGetError(out var value) ? value : null,
            error => RawFrom(error, error.TryGetNoContent),
            isWrite: true,
            cancellationToken);

        return MapRefund(refund);
    }

    public async Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        var refund = await ExecuteTypedAsync<Refund, GetRefundError>(
            (options, ct) => _client.Payments.GetRefund(
                refundId: refundId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: options,
                ct: ct),
            error => error.TryGetError(out var value) ? value : null,
            error => RawFrom(error, error.TryGetNoContent),
            isWrite: false,
            cancellationToken);

        return MapRefund(refund);
    }

    public async Task<ProviderPaymentMethod> SavePaymentMethodAsync(string buyerId, CardInput card,
        string requestId, CancellationToken cancellationToken)
    {
        var token = await ExecuteTypedAsync<PaymentTokenResponse, CreatePaymentTokenError>(
            (options, ct) => _client.Vault.CreatePaymentToken(
                payPalRequestId: requestId,
                body: new PaymentTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = StableCustomerId(buyerId) },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Name = card.Name,
                            Number = DigitsOnly(card.Number),
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            BillingAddress = ToAddress(card.BillingAddress)
                        }
                    }
                },
                requestOptions: options,
                ct: ct),
            error => error.TryGetError(out var value) ? value : null,
            RawFrom,
            isWrite: true,
            cancellationToken);

        var tokenCard = token.PaymentSource?.Card;
        if (token.Id is null || tokenCard?.LastDigits is null || tokenCard.Brand is null || tokenCard.Expiry is null)
        {
            throw ProviderSchemaError("PayPal did not return recognizable saved-card details.");
        }

        return new ProviderPaymentMethod(token.Id, token.Customer?.Id, tokenCard.Brand.Value,
            tokenCard.LastDigits, tokenCard.Expiry);
    }

    public async Task DeletePaymentMethodAsync(string vaultId, CancellationToken cancellationToken)
    {
        await ExecuteTypedAsync<object, DeletePaymentTokenError>(
            async (options, ct) =>
            {
                await _client.Vault.DeletePaymentToken(vaultId, requestOptions: options, ct: ct);
                return new object();
            },
            error => error.TryGetError(out var value) ? value : null,
            RawFrom,
            isWrite: true,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to <= from)
        {
            throw new PaymentException(PaymentFailureKind.Validation, "The reconciliation end must be after its start.");
        }

        using var reportBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        reportBudget.CancelAfter(TimeSpan.FromMinutes(5));
        var transactions = new List<ProviderTransaction>();
        var rangeStart = FloorToSecond(from);
        var reportEnd = CeilingToSecond(to);

        while (rangeStart <= reportEnd)
        {
            var maximumRangeEnd = rangeStart.AddDays(31).AddSeconds(-1);
            var rangeEnd = maximumRangeEnd < reportEnd ? maximumRangeEnd : reportEnd;
            var page = 1;
            const int pageSize = 100;
            const int maximumPagesPerRange = 10_000;

            while (page <= maximumPagesPerRange)
            {
                var response = await ExecuteRawAsync(
                    (options, ct) => _client.TransactionSearch.SearchTransactions(
                        startDate: ProviderDate(rangeStart),
                        endDate: ProviderDate(rangeEnd),
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
                        pageSize: pageSize,
                        page: page,
                        requestOptions: options,
                        ct: ct),
                    reportBudget.Token);

                foreach (var detail in response.TransactionDetails ?? [])
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    transactions.Add(new ProviderTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.InvoiceId,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        ParseDate(info.TransactionInitiationDate),
                        ParseNullableMoney(info.TransactionAmount?.Value),
                        ParseNullableMoney(info.FeeAmount?.Value),
                        info.TransactionAmount?.CurrencyCode));
                }

                if (response.TotalPages is { } totalPages)
                {
                    if (page >= totalPages)
                    {
                        break;
                    }
                }
                else if ((response.TransactionDetails?.Count ?? 0) < pageSize)
                {
                    break;
                }

                page++;
            }

            if (page > maximumPagesPerRange)
            {
                throw new PaymentException(PaymentFailureKind.ProviderUnavailable,
                    "PayPal reconciliation exceeded its safety page limit; narrow the requested range.");
            }

            rangeStart = rangeEnd.AddSeconds(1);
        }

        return transactions
            .Where(transaction => transaction.InitiatedAt is null ||
                                  transaction.InitiatedAt >= from && transaction.InitiatedAt <= to)
            .ToList();
    }

    private async Task<T> ExecuteTypedAsync<T, TError>(
        Func<RequestOptions, CancellationToken, Task<T>> operation,
        Func<TError, Error?> typedError,
        Func<TError, RawError?> rawError,
        bool isWrite,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        HttpStatusCode? observedStatus = null;
        var requestOptions = ObserveStatus(status => observedStatus = status);

        try
        {
            return await operation(requestOptions, budget.Token);
        }
        catch (SdkException<TError> exception)
        {
            var error = typedError(exception.Error);
            var raw = rawError(exception.Error);
            throw Translate(error, raw?.StatusCode ?? observedStatus, isWrite, exception);
        }
        catch (JsonException exception)
        {
            throw TranslateJson(observedStatus, isWrite, exception);
        }
        catch (HttpRequestException exception)
        {
            throw TransportFailure(isWrite, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw TransportFailure(isWrite, exception);
        }
    }

    private async Task<SearchResponse> ExecuteRawAsync(
        Func<RequestOptions, CancellationToken, Task<SearchResponse>> operation,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        HttpStatusCode? observedStatus = null;
        var requestOptions = ObserveStatus(status => observedStatus = status);

        try
        {
            return await operation(requestOptions, budget.Token);
        }
        catch (SdkException<RawError> exception)
        {
            Error? error = null;
            try
            {
                error = exception.Error.ReadAsJson<Error>();
            }
            catch (JsonException)
            {
                // The HTTP status still produces a safe, actionable classification below.
            }
            throw Translate(error, exception.Error.StatusCode, isWrite: false, exception);
        }
        catch (JsonException exception)
        {
            throw TranslateJson(observedStatus, isWrite: false, exception);
        }
        catch (HttpRequestException exception)
        {
            throw TransportFailure(isWrite: false, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw TransportFailure(isWrite: false, exception);
        }
    }

    private PaymentException Translate(Error? error, HttpStatusCode? status, bool isWrite, Exception exception)
    {
        var issue = error?.Details?.FirstOrDefault()?.Issue;
        if (error?.DebugId is { Length: > 0 } debugId)
        {
            _logger.LogWarning(exception, "PayPal rejected an operation. DebugId: {DebugId}; Name: {Name}; Issue: {Issue}",
                debugId, error.Name, issue);
        }

        if (string.Equals(error?.Name, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return new PaymentException(PaymentFailureKind.PayerActionRequired,
                "PayPal requires browser approval for this card payment.", error?.DebugId, exception);
        }

        var kind = status switch
        {
            HttpStatusCode.NotFound => PaymentFailureKind.NotFound,
            HttpStatusCode.Conflict => PaymentFailureKind.Conflict,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => PaymentFailureKind.ProviderRejected,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PaymentFailureKind.ProviderUnavailable,
            (HttpStatusCode)429 => PaymentFailureKind.ProviderUnavailable,
            >= HttpStatusCode.InternalServerError => PaymentFailureKind.ProviderUnavailable,
            _ => isWrite ? PaymentFailureKind.UnknownOutcome : PaymentFailureKind.ProviderUnavailable
        };

        var message = kind == PaymentFailureKind.ProviderRejected && error is not null
            ? $"PayPal rejected the request: {error.Name}. {error.Message}"
            : kind == PaymentFailureKind.Conflict && error is not null
                ? $"PayPal could not complete the operation: {error.Name}. {error.Message}"
                : "PayPal is temporarily unavailable.";

        return new PaymentException(kind, message, error?.DebugId, exception);
    }

    private static PaymentException TranslateJson(HttpStatusCode? status, bool isWrite, JsonException exception)
    {
        if (status is >= HttpStatusCode.BadRequest)
        {
            return new PaymentException(PaymentFailureKind.ProviderRejected,
                "PayPal rejected the request but returned an unreadable error response.", innerException: exception);
        }

        return new PaymentException(isWrite ? PaymentFailureKind.UnknownOutcome : PaymentFailureKind.ProviderUnavailable,
            "PayPal returned a response that could not be processed.", innerException: exception);
    }

    private static PaymentException TransportFailure(bool isWrite, Exception exception) =>
        new(isWrite ? PaymentFailureKind.UnknownOutcome : PaymentFailureKind.ProviderUnavailable,
            isWrite
                ? "The PayPal operation may have completed, but its result could not be confirmed. Retry the same operation."
                : "PayPal is temporarily unavailable.",
            innerException: exception);

    private static PaymentException ProviderSchemaError(string message) =>
        new(PaymentFailureKind.ProviderUnavailable, message);

    private static RequestOptions ObserveStatus(Action<HttpStatusCode> observer) => new()
    {
        Hooks = [SdkHook.OnResponse((response, _) => observer(response.StatusCode))]
    };

    private static RawError? RawFrom<TError>(TError error) where TError : ApiError =>
        error.TryGetRawError(out var raw) ? raw : null;

    private delegate bool RawAccessor(out RawError value);

    private static RawError? RawFrom<TError>(TError error, RawAccessor special) where TError : ApiError
    {
        if (special(out var raw))
        {
            return raw;
        }

        return RawFrom(error);
    }

    private static ProviderAuthorization MapAuthorization(PaymentAuthorization authorization, string orderId)
    {
        if (authorization.Id is null || authorization.Status is null || authorization.Amount is null)
        {
            throw ProviderSchemaError("PayPal did not return complete authorization details.");
        }

        return new ProviderAuthorization(orderId, authorization.Id, authorization.Status.Value,
            ParseMoney(authorization.Amount.Value), authorization.Amount.CurrencyCode,
            ParseDate(authorization.CreateTime), ParseDate(authorization.ExpirationTime));
    }

    private static ProviderCapture MapCapture(CapturedPayment capture)
    {
        if (capture.Id is null || capture.Status is null || capture.Amount is null)
        {
            throw ProviderSchemaError("PayPal did not return complete capture details.");
        }

        var breakdown = capture.SellerReceivableBreakdown;
        if (capture.Status == CaptureStatus.Completed &&
            (breakdown?.PaypalFee is null || breakdown.NetAmount is null))
        {
            throw ProviderSchemaError("PayPal completed the capture without returning fee and net proceeds.");
        }

        return new ProviderCapture(capture.Id, capture.Status.Value, ParseMoney(capture.Amount.Value),
            capture.Amount.CurrencyCode,
            ParseNullableMoney(breakdown?.PaypalFee?.Value) ?? 0m,
            ParseNullableMoney(breakdown?.NetAmount?.Value) ?? 0m);
    }

    private static ProviderRefund MapRefund(Refund refund)
    {
        if (refund.Id is null || refund.Status is null || refund.Amount is null)
        {
            throw ProviderSchemaError("PayPal did not return complete refund details.");
        }

        return new ProviderRefund(refund.Id, refund.Status.Value, ParseMoney(refund.Amount.Value),
            refund.Amount.CurrencyCode, ParseDate(refund.CreateTime) ?? DateTimeOffset.UtcNow);
    }

    private static CardRequest ToCardRequest(CardInput card) => new()
    {
        Name = card.Name,
        Number = DigitsOnly(card.Number),
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = ToAddress(card.BillingAddress)
    };

    private static Address ToAddress(BillingAddressInput address) => new()
    {
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.City,
        AdminArea1 = address.State,
        PostalCode = address.PostalCode,
        CountryCode = address.CountryCode.ToUpperInvariant()
    };

    private static string DigitsOnly(string cardNumber) =>
        new(cardNumber.Where(char.IsDigit).ToArray());

    private static string StableCustomerId(string buyerId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(buyerId));
        return "eshop-" + Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    private static string MoneyText(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string ProviderDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset FloorToSecond(DateTimeOffset value)
    {
        var utcTicks = value.ToUniversalTime().Ticks;
        return new DateTimeOffset(utcTicks - utcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }

    private static DateTimeOffset CeilingToSecond(DateTimeOffset value)
    {
        var floor = FloorToSecond(value);
        return floor == value.ToUniversalTime() ? floor : floor.AddSeconds(1);
    }

    private static decimal ParseMoney(string value) => decimal.Parse(value, NumberStyles.Number,
        CultureInfo.InvariantCulture);

    private static decimal? ParseNullableMoney(string? value) => decimal.TryParse(value, NumberStyles.Number,
        CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseDate(string? value) => DateTimeOffset.TryParse(value,
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
}
