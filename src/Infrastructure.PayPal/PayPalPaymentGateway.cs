using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Requests.Orders;
using PayPalServerSdk.Requests.Payments;
using PayPalServerSdk.Requests.TransactionSearch;
using PayPalServerSdk.Requests.Vault;
using PayPalError = PayPalServerSdk.Models.Error;
using PayPalOrder = PayPalServerSdk.Models.Order;
using PayPalOrderStatus = PayPalServerSdk.Models.Enums.OrderStatus;
using PayPalRefund = PayPalServerSdk.Models.Refund;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// <see cref="IPaymentGateway"/> over the PayPal Server SDK. This is the one boundary where SDK
/// exceptions become <see cref="PaymentProviderException"/>; every call runs under the request's
/// <see cref="PayPalCallBudget"/>. Card data is passed through to PayPal and never logged.
/// </summary>
public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private const string ReturnRepresentation = "return=representation";

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalCallBudget _budget;
    private readonly PayPalResilienceSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(
        PayPalServerSdkClient client,
        IOptions<PayPalOptions> options,
        PayPalCallBudget budget,
        PayPalResilienceSettings settings,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _budget = budget;
        _settings = settings;
        _logger = logger;
        Currency = options.Value.Currency!.Trim().ToUpperInvariant();
    }

    public string Currency { get; }

    /// <summary>SearchTransactions accepts at most 31 days between start and end.</summary>
    public TimeSpan MaxSearchRange => TimeSpan.FromDays(31);

    // ------------------------------------------------------------------ authorize

    public async Task<ProviderAuthorization> AuthorizeAsync(AuthorizePaymentCommand command, CancellationToken cancellationToken = default)
    {
        var card = command.Card is { } c
            ? new CardRequest
            {
                Name = c.NameOnCard,
                Number = c.Number,
                Expiry = c.Expiry,
                SecurityCode = c.SecurityCode,
                BillingAddress = MapAddress(c.BillingAddress),
            }
            : new CardRequest { VaultId = command.SavedCardToken };

        var request = new CreateOrderRequest
        {
            // Mandatory for a single-step create with a card/vault payment source; replaying it returns
            // the original order instead of holding the money twice.
            PayPalRequestId = command.RequestId,
            Prefer = ReturnRepresentation,
            Body = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits =
                [
                    new PurchaseUnitRequest
                    {
                        ReferenceId = $"order-{command.OrderId}",
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = Currency,
                            Value = CurrencyRules.Format(command.Amount, Currency),
                        },
                        InvoiceId = command.InvoiceId,
                        CustomId = command.CustomId,
                        Description = command.Description,
                    },
                ],
                PaymentSource = new PaymentSource { Card = card },
            },
        };

        var order = await CallAsync("CreateOrder", isWrite: true,
            ct => _client.Orders.CreateOrder(request, cancellationToken: ct), cancellationToken);

        if (order.Status == PayPalOrderStatus.PayerActionRequired)
            throw PayerActionRequired($"PayPal order {order.Id}");

        var authorization = LatestAuthorization(order.PurchaseUnits);
        var cardResponse = order.PaymentSource?.Card;
        if (authorization is null)
        {
            if (string.IsNullOrEmpty(order.Id) ||
                (order.Status != PayPalOrderStatus.Approved && order.Status != PayPalOrderStatus.Created && order.Status != PayPalOrderStatus.Saved))
            {
                throw new PaymentProviderException(PaymentProviderErrorKind.Rejected,
                    $"PayPal returned order {order.Id} with status {order.Status?.Value ?? "unknown"} and no authorization.");
            }

            (authorization, cardResponse) = await AuthorizeApprovedOrderAsync(order.Id, command.RequestId, cardResponse, cancellationToken);
        }

        var result = new ProviderAuthorization(
            order.Id!,
            authorization.Id ?? throw Malformed("authorization id"),
            MapAuthorizationOutcome(authorization.Status),
            authorization.Status?.Value ?? "UNKNOWN",
            ParseAmount(authorization.Amount),
            ParseTime(authorization.CreateTime),
            ParseTime(authorization.ExpirationTime),
            cardResponse?.Brand?.Value,
            cardResponse?.LastDigits);

        _logger.LogInformation("PayPal CreateOrder {ProviderOrderId}: authorization {AuthorizationId} {Status} for {Amount} {Currency} (eShop order {OrderId}).",
            result.ProviderOrderId, result.AuthorizationId, result.ProviderStatus, result.Amount, Currency, command.OrderId);
        return result;
    }

    private async Task<(AuthorizationWithAdditionalData, CardResponse?)> AuthorizeApprovedOrderAsync(
        string providerOrderId, string requestId, CardResponse? card, CancellationToken cancellationToken)
    {
        var request = new AuthorizeOrderRequest
        {
            Id = providerOrderId,
            PayPalRequestId = $"{requestId}-authorize",
            Prefer = ReturnRepresentation,
        };

        try
        {
            var response = await CallAsync("AuthorizeOrder", isWrite: true,
                ct => _client.Orders.AuthorizeOrder(request, cancellationToken: ct), cancellationToken);
            if (response.Status == PayPalOrderStatus.PayerActionRequired)
                throw PayerActionRequired($"PayPal order {providerOrderId}");
            var authorization = LatestAuthorization(response.PurchaseUnits)
                ?? throw Malformed($"authorization on order {providerOrderId}");
            return (authorization, response.PaymentSource?.Card ?? card);
        }
        catch (PaymentProviderException ex) when (ex.IsUnknownOutcome)
        {
            // Settle from PayPal's own record of the order before reporting anything.
            var order = await CallAsync("GetOrder", isWrite: false,
                ct => _client.Orders.GetOrder(new GetOrderRequest { Id = providerOrderId }, cancellationToken: ct), cancellationToken);
            var authorization = LatestAuthorization(order.PurchaseUnits);
            if (authorization is null)
                throw;
            return (authorization, order.PaymentSource?.Card ?? card);
        }
    }

    // ------------------------------------------------------------------ authorization state

    public async Task<ProviderAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await CallAsync("GetAuthorizedPayment", isWrite: false,
            ct => _client.Payments.GetAuthorizedPayment(new GetAuthorizedPaymentRequest { AuthorizationId = authorizationId }, cancellationToken: ct),
            cancellationToken);
        return MapState(authorization, authorizationId);
    }

    public async Task<ProviderAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new ReauthorizePaymentRequest
        {
            AuthorizationId = authorizationId,
            PayPalRequestId = requestId,
            Prefer = ReturnRepresentation,
            Body = new ReauthorizeRequest { Amount = Money(amount) },
        };
        var authorization = await CallAsync("ReauthorizePayment", isWrite: true,
            ct => _client.Payments.ReauthorizePayment(request, cancellationToken: ct), cancellationToken);
        var state = MapState(authorization, authorizationId);
        _logger.LogInformation("PayPal ReauthorizePayment {AuthorizationId} -> {NewAuthorizationId} {Status}.", authorizationId, state.AuthorizationId, state.ProviderStatus);
        return state;
    }

    public async Task<ProviderAuthorizationState> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new VoidPaymentRequest
        {
            AuthorizationId = authorizationId,
            PayPalRequestId = requestId,
            Prefer = ReturnRepresentation,
        };
        var authorization = await CallAsync("VoidPayment", isWrite: true,
            ct => _client.Payments.VoidPayment(request, cancellationToken: ct), cancellationToken);
        var state = MapState(authorization, authorizationId);
        _logger.LogInformation("PayPal VoidPayment {AuthorizationId}: {Status}.", authorizationId, state.ProviderStatus);
        return state;
    }

    // ------------------------------------------------------------------ capture

    public async Task<ProviderCapture> CaptureAsync(string authorizationId, decimal amount, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new CaptureAuthorizedPaymentRequest
        {
            AuthorizationId = authorizationId,
            PayPalRequestId = requestId,
            Prefer = ReturnRepresentation,
            Body = new CaptureRequest
            {
                Amount = Money(amount),
                FinalCapture = true,
            },
        };
        var capture = await CallAsync("CaptureAuthorizedPayment", isWrite: true,
            ct => _client.Payments.CaptureAuthorizedPayment(request, cancellationToken: ct), cancellationToken);
        var result = MapCapture(capture.Id, capture.Status, capture.Amount, capture.SellerReceivableBreakdown);
        _logger.LogInformation("PayPal CaptureAuthorizedPayment {AuthorizationId}: capture {CaptureId} {Status}, gross {Gross}, fee {Fee}, net {Net}.",
            authorizationId, result.CaptureId, result.ProviderStatus, result.Amount, result.PayPalFee, result.NetAmount);
        return result;
    }

    public async Task<ProviderCapture?> FindCaptureAsync(string providerOrderId, CancellationToken cancellationToken = default)
    {
        var order = await CallAsync("GetOrder", isWrite: false,
            ct => _client.Orders.GetOrder(new GetOrderRequest { Id = providerOrderId }, cancellationToken: ct), cancellationToken);
        var capture = order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Captures ?? [])
            .LastOrDefault(c => c.Status != CaptureStatus.Declined && c.Status != CaptureStatus.Failed);
        return capture is null ? null : MapCapture(capture.Id, capture.Status, capture.Amount, capture.SellerReceivableBreakdown);
    }

    public async Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default)
    {
        var capture = await CallAsync("GetCapturedPayment", isWrite: false,
            ct => _client.Payments.GetCapturedPayment(new GetCapturedPaymentRequest { CaptureId = captureId }, cancellationToken: ct), cancellationToken);
        return MapCapture(capture.Id ?? captureId, capture.Status, capture.Amount, capture.SellerReceivableBreakdown);
    }

    // ------------------------------------------------------------------ refund

    public async Task<ProviderRefund> RefundAsync(string captureId, decimal amount, string requestId, string customId, CancellationToken cancellationToken = default)
    {
        var request = new RefundCapturedPaymentRequest
        {
            CaptureId = captureId,
            PayPalRequestId = requestId,
            Prefer = ReturnRepresentation,
            Body = new RefundRequest
            {
                Amount = Money(amount),
                CustomId = customId,
            },
        };
        var refund = await CallAsync("RefundCapturedPayment", isWrite: true,
            ct => _client.Payments.RefundCapturedPayment(request, cancellationToken: ct), cancellationToken);
        var result = MapRefund(refund);
        _logger.LogInformation("PayPal RefundCapturedPayment {CaptureId}: refund {RefundId} {Status} {Amount} {Currency}.",
            captureId, result.RefundId, result.ProviderStatus, result.Amount, Currency);
        return result;
    }

    // ------------------------------------------------------------------ vault

    public async Task<ProviderSavedCard> SaveCardAsync(CardDetails card, string? providerCustomerId, string requestId, CancellationToken cancellationToken = default)
    {
        var setupRequest = new CreateSetupTokenRequest
        {
            PayPalRequestId = requestId,
            Body = new SetupTokenRequest
            {
                Customer = providerCustomerId is null ? null : new Customer { Id = providerCustomerId },
                PaymentSource = new SetupTokenRequestPaymentSource
                {
                    Card = new SetupTokenRequestCard
                    {
                        Name = card.NameOnCard,
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        BillingAddress = MapAddress(card.BillingAddress),
                    },
                },
            },
        };

        // A setup token moves no money, so an unknown outcome is settled by replaying the same request id.
        var setup = await WithReplayAsync(() => CallAsync("CreateSetupToken", isWrite: true,
            ct => _client.Vault.CreateSetupToken(setupRequest, cancellationToken: ct), cancellationToken));

        if (setup.Status == PaymentTokenStatus.PayerActionRequired)
            throw PayerActionRequired($"PayPal setup token {setup.Id}");
        if (string.IsNullOrEmpty(setup.Id))
            throw Malformed("setup token id");

        var tokenRequest = new CreatePaymentTokenRequest
        {
            PayPalRequestId = $"{requestId}-token",
            Body = new PaymentTokenRequest
            {
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Token = new VaultTokenRequest { Id = setup.Id, Type = VaultTokenRequestType.SetupToken },
                },
            },
        };
        var token = await WithReplayAsync(() => CallAsync("CreatePaymentToken", isWrite: true,
            ct => _client.Vault.CreatePaymentToken(tokenRequest, cancellationToken: ct), cancellationToken));

        var vaulted = token.PaymentSource?.Card;
        var result = new ProviderSavedCard(
            token.Id ?? throw Malformed("payment token id"),
            token.Customer?.Id ?? setup.Customer?.Id ?? providerCustomerId,
            vaulted?.Brand?.Value,
            vaulted?.LastDigits,
            vaulted?.Expiry);
        _logger.LogInformation("PayPal vaulted card as payment token {TokenId} for customer {CustomerId} ({Brand} ending {Last4}).",
            result.PaymentTokenId, result.ProviderCustomerId ?? "", result.Brand ?? "card", result.LastDigits ?? "");
        return result;
    }

    public async Task DeleteSavedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await CallAsync("DeletePaymentToken", isWrite: true, async ct =>
            {
                await _client.Vault.DeletePaymentToken(new DeletePaymentTokenRequest { Id = paymentTokenId }, cancellationToken: ct);
                return true;
            }, cancellationToken);
            _logger.LogInformation("PayPal deleted payment token {TokenId}.", paymentTokenId);
        }
        catch (PaymentProviderException ex) when (ex.ProviderStatusCode == (int)HttpStatusCode.NotFound)
        {
            // Already gone at PayPal: the desired end state.
        }
    }

    // ------------------------------------------------------------------ reporting

    public async Task<ProviderTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, int page, CancellationToken cancellationToken = default)
    {
        var request = new SearchTransactionsRequest
        {
            StartDate = ToRfc3339(from),
            EndDate = ToRfc3339(to),
            Fields = "transaction_info",
            BalanceAffectingRecordsOnly = "Y",
            PageSize = 500,
            Page = page,
        };
        var response = await CallAsync("SearchTransactions", isWrite: false,
            ct => _client.TransactionSearch.SearchTransactions(request, cancellationToken: ct), cancellationToken);

        var transactions = (response.TransactionDetails ?? [])
            .Select(d => d.TransactionInfo)
            .Where(i => !string.IsNullOrEmpty(i?.TransactionId))
            .Select(i => new ProviderTransaction(
                i!.TransactionId!,
                i.TransactionEventCode,
                i.TransactionStatus,
                ParseTime(i.TransactionInitiationDate),
                CurrencyRules.Parse(i.TransactionAmount?.Value),
                i.TransactionAmount?.CurrencyCode,
                CurrencyRules.Parse(i.FeeAmount?.Value),
                i.InvoiceId,
                i.CustomField,
                i.PaypalReferenceId))
            .ToList();

        return new ProviderTransactionPage(transactions, response.Page ?? page, response.TotalPages ?? page);
    }

    // ------------------------------------------------------------------ the boundary

    private async Task<T> CallAsync<T>(string operation, bool isWrite, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        // A write leaves part of the budget unused so its outcome can still be settled by a re-read.
        using var deadline = _budget.Link(cancellationToken, isWrite ? _settings.SettlementReserve : TimeSpan.Zero);
        try
        {
            return await call(deadline.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own deadline fired, not the caller's cancellation.
            _logger.LogWarning("PayPal {Operation} did not respond within the request time budget.", operation);
            throw isWrite
                ? Unknown(operation, ex)
                : new PaymentProviderException(PaymentProviderErrorKind.Timeout, "PayPal did not respond in time.", innerException: ex);
        }
        catch (ResponseDeserializationException ex)
        {
            if ((int)ex.StatusCode is >= 200 and < 300)
            {
                _logger.LogError(ex, "PayPal {Operation} answered {Status} with a body that could not be read.", operation, (int)ex.StatusCode);
                throw isWrite
                    ? Unknown(operation, ex)
                    : new PaymentProviderException(PaymentProviderErrorKind.Unavailable, "PayPal returned a response that could not be processed.", (int)ex.StatusCode, innerException: ex);
            }
            throw Translate(operation, isWrite, ex, (int)ex.StatusCode, error: null);
        }
        catch (ApiException ex)
        {
            throw Translate(operation, isWrite, ex, (int)ex.StatusCode, ExtractError(ex));
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning("PayPal {Operation} timed out after {Timeout}.", operation, ex.Timeout);
            throw isWrite
                ? Unknown(operation, ex)
                : new PaymentProviderException(PaymentProviderErrorKind.Timeout, "PayPal did not respond in time.", innerException: ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning("PayPal {Operation} could not be reached: {Reason}", operation, ex.InnerException?.Message ?? ex.GetType().Name);
            throw isWrite
                ? Unknown(operation, ex)
                : new PaymentProviderException(PaymentProviderErrorKind.Unavailable, "PayPal could not be reached.", innerException: ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex.InnerException, "PayPal rejected the merchant credentials during {Operation}.", operation);
            throw new PaymentProviderException(PaymentProviderErrorKind.Unavailable,
                "PayPal rejected the merchant's API credentials; payments are unavailable.", innerException: ex);
        }
    }

    private PaymentProviderException Translate(string operation, bool isWrite, Exception ex, int status, PayPalError? error)
    {
        var issues = error?.Details?.Select(d => d.Issue).Where(i => !string.IsNullOrEmpty(i)).ToList() ?? new List<string>();
        var detail = error?.Details?.Select(d => d.Description).FirstOrDefault(d => !string.IsNullOrEmpty(d));
        var message = error is null
            ? $"PayPal answered {status}."
            : detail is null ? error.Message : $"{error.Message} {detail}";

        _logger.LogWarning("PayPal {Operation} failed with {Status} {Name} (debug_id {DebugId}) issues [{Issues}].",
            operation, status, error?.Name ?? "", error?.DebugId ?? "", string.Join(",", issues));

        var kind = status switch
        {
            401 or 403 => PaymentProviderErrorKind.Unavailable, // our credentials/permissions, not the caller's fault
            429 => PaymentProviderErrorKind.Unavailable,
            >= 500 => isWrite ? PaymentProviderErrorKind.OutcomeUnknown : PaymentProviderErrorKind.Unavailable,
            _ => PaymentProviderErrorKind.Rejected,
        };
        if (status is 401 or 403)
            message = "PayPal refused the merchant's credentials or permissions for this operation.";

        return new PaymentProviderException(kind, message, status, error?.Name, error?.DebugId, issues, ex);
    }

    private static PayPalError? ExtractError(ApiException ex) => ex switch
    {
        ApiException<CreateOrderError> e => e.Error.TryGetError(out var x) ? x : null,
        ApiException<AuthorizeOrderError> e => e.Error.TryGetError(out var x) ? x : null,
        ApiException<GetOrderError> e => e.Error.TryGetError(out var x) ? x : null,
        ApiException<GetAuthorizedPaymentError> e => e.Error.TryGetError(out var x) ? x : null,
        ApiException<ReauthorizePaymentError> e => e.Error.TryGetError(out var x) ? x : null,
        ApiException<CaptureAuthorizedPaymentError> e => e.Error.TryGetError(out var x) ? x : null,
        ApiException<GetCapturedPaymentError> e => e.Error.TryGetError(out var x) ? x : null,
        ApiException<VoidPaymentError> e => e.Error.TryGetError(out var x) ? x : null,
        ApiException<RefundCapturedPaymentError> e => e.Error.TryGetError(out var x) ? x : null,
        ApiException<CreateSetupTokenError> e => e.Error.TryGetError(out var x) ? x : null,
        ApiException<CreatePaymentTokenError> e => e.Error.TryGetError(out var x) ? x : null,
        ApiException<DeletePaymentTokenError> e => e.Error.TryGetError(out var x) ? x : null,
        ApiException<RawError> e => TryReadRawError(e.Error),
        _ => null,
    };

    private static PayPalError? TryReadRawError(RawError raw)
    {
        try
        {
            return raw.ReadAsJson<PayPalError>();
        }
        catch (Exception)
        {
            return null; // not JSON, or not PayPal's error shape: the status still carries the meaning
        }
    }

    private async Task<T> WithReplayAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (PaymentProviderException ex) when (ex.Kind == PaymentProviderErrorKind.OutcomeUnknown && !_budget.IsExhausted)
        {
            return await call();
        }
    }

    private static PaymentProviderException Unknown(string operation, Exception ex) =>
        new(PaymentProviderErrorKind.OutcomeUnknown, $"PayPal did not confirm the {operation} request.", innerException: ex);

    private static PaymentProviderException Malformed(string what) =>
        new(PaymentProviderErrorKind.Unavailable, $"PayPal's response did not include the {what}.");

    private static PaymentProviderException PayerActionRequired(string what) =>
        new(PaymentProviderErrorKind.PayerActionRequired,
            $"{what} requires the shopper to complete an approval step (such as 3-D Secure) in a browser, which this API does not support. No money was taken.");

    // ------------------------------------------------------------------ mapping

    private Money Money(decimal amount) => new() { CurrencyCode = Currency, Value = CurrencyRules.Format(amount, Currency) };

    private static Address? MapAddress(CardBillingAddress? address) => address is null
        ? null
        : new Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode,
        };

    private static AuthorizationWithAdditionalData? LatestAuthorization(IReadOnlyList<PurchaseUnit>? units) =>
        units?.SelectMany(u => u.Payments?.Authorizations ?? []).LastOrDefault();

    private static ProviderAuthorizationState MapState(PaymentAuthorization authorization, string fallbackId) => new(
        authorization.Id ?? fallbackId,
        MapAuthorizationOutcome(authorization.Status),
        authorization.Status?.Value ?? "UNKNOWN",
        ParseAmount(authorization.Amount),
        ParseTime(authorization.CreateTime),
        ParseTime(authorization.ExpirationTime));

    private static AuthorizationOutcome MapAuthorizationOutcome(AuthorizationStatus? status) => status is null
        ? AuthorizationOutcome.Pending
        : status.Match(
            onCreated: () => AuthorizationOutcome.Approved,
            onCaptured: () => AuthorizationOutcome.Captured,
            onDenied: () => AuthorizationOutcome.Denied,
            onPartiallyCaptured: () => AuthorizationOutcome.Captured,
            onVoided: () => AuthorizationOutcome.Voided,
            onPending: () => AuthorizationOutcome.Pending,
            otherwise: _ => AuthorizationOutcome.NotCapturable);

    private static ProviderCapture MapCapture(string? id, CaptureStatus? status, Money? amount, SellerReceivableBreakdown? breakdown)
    {
        var outcome = status is null
            ? CaptureOutcome.Pending
            : status.Match(
                onCompleted: () => CaptureOutcome.Completed,
                onDeclined: () => CaptureOutcome.Failed,
                onPartiallyRefunded: () => CaptureOutcome.PartiallyRefunded,
                onPending: () => CaptureOutcome.Pending,
                onRefunded: () => CaptureOutcome.Refunded,
                onFailed: () => CaptureOutcome.Failed,
                otherwise: _ => CaptureOutcome.Pending);

        return new ProviderCapture(
            id ?? throw Malformed("capture id"),
            outcome,
            status?.Value ?? "UNKNOWN",
            ParseAmount(amount) ?? ParseAmount(breakdown?.GrossAmount),
            ParseAmount(breakdown?.PaypalFee),
            ParseAmount(breakdown?.NetAmount));
    }

    private static ProviderRefund MapRefund(PayPalRefund refund)
    {
        var outcome = refund.Status is null
            ? RefundOutcome.Pending
            : refund.Status.Match(
                onCancelled: () => RefundOutcome.Failed,
                onFailed: () => RefundOutcome.Failed,
                onPending: () => RefundOutcome.Pending,
                onCompleted: () => RefundOutcome.Completed,
                otherwise: _ => RefundOutcome.Pending);
        return new ProviderRefund(refund.Id ?? throw Malformed("refund id"), outcome, refund.Status?.Value ?? "UNKNOWN", ParseAmount(refund.Amount));
    }

    private static decimal? ParseAmount(Money? money) => CurrencyRules.Parse(money?.Value);

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    private static string ToRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
