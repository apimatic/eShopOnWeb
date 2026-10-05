using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>How the shopper pays: a one-off card, or one of their saved cards. Exactly one is set.</summary>
public sealed record PaymentInstrument(CardDetails? Card, int? PaymentMethodId);

public sealed record PaymentOperationResult(Order Order, Payment? Payment, bool AlreadyDone);

public sealed record RefundOperationResult(Order Order, Payment Payment, PaymentRefund Refund, bool Replayed);

/// <summary>
/// Moves money for an order: authorize (hold) at checkout, capture at fulfilment, void on cancel,
/// refund on return. Every write runs under the order's claim (<see cref="OrderPaymentLock"/>),
/// persists its provider idempotency key before calling the provider, and settles an unknown
/// outcome by re-reading or replaying that key instead of reporting a failure.
/// </summary>
public class PaymentService
{
    /// <summary>Provider honor period after which a hold should be renewed before capture.</summary>
    public static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    /// <summary>Provider authorization period; past it a hold cannot be renewed.</summary>
    public static readonly TimeSpan AuthorizationPeriod = TimeSpan.FromDays(29);

    private static readonly Regex IdempotencyKeyPattern = new("^[A-Za-z0-9_.:-]{1,64}$", RegexOptions.Compiled);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<Payment> _payments;
    private readonly IReadRepository<PaymentMethod> _paymentMethods;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentClaimStore _claims;
    private readonly TimeProvider _clock;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orders,
        IRepository<Payment> payments,
        IReadRepository<PaymentMethod> paymentMethods,
        IPaymentGateway gateway,
        IPaymentClaimStore claims,
        TimeProvider clock,
        IAppLogger<PaymentService> logger)
    {
        _orders = orders;
        _payments = payments;
        _paymentMethods = paymentMethods;
        _gateway = gateway;
        _claims = claims;
        _clock = clock;
        _logger = logger;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    // ------------------------------------------------------------------ authorize

    public async Task<PaymentOperationResult> PayAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken cancellationToken = default)
    {
        if ((instrument.Card is null) == (instrument.PaymentMethodId is null))
            throw new PaymentValidationException("Provide either card details or a saved paymentMethodId, not both.");

        string? savedCardToken = null;
        string? lastDigits;
        if (instrument.PaymentMethodId is int paymentMethodId)
        {
            // The vault token must be one this app saved for THIS buyer and not removed since.
            var method = await _paymentMethods.FirstOrDefaultAsync(new PaymentMethodForBuyerSpec(paymentMethodId, buyerId), cancellationToken);
            if (method is null || method.IsRemoved || string.IsNullOrEmpty(method.CardId))
                throw new PaymentResourceNotFoundException($"Saved card {paymentMethodId} was not found.");
            savedCardToken = method.CardId;
            lastDigits = method.Last4;
        }
        else
        {
            CardValidator.Validate(instrument.Card!, Now);
            lastDigits = instrument.Card!.LastDigits;
        }

        await using var orderLock = await OrderPaymentLock.AcquireAsync(_claims, orderId, cancellationToken);
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _payments.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        switch (order.Status)
        {
            case OrderStatus.Cancelled:
                throw new PaymentConflictException($"Order {orderId} is cancelled.", "ORDER_CANCELLED");
            case OrderStatus.PaymentAuthorized:
            case OrderStatus.Fulfilled:
                return new PaymentOperationResult(order, payment, AlreadyDone: true);
        }

        if (payment is not { Status: PaymentStatus.AuthorizationPending })
        {
            var amount = order.Total();
            CurrencyRules.EnsureRepresentable(amount, _gateway.Currency, "Order total");
            if (amount <= 0)
                throw new PaymentValidationException($"Order {orderId} has nothing to pay.");

            var requestId = $"eshop-{orderId}-auth-{Guid.NewGuid():N}";
            var invoiceId = $"eshop-{orderId}-{Guid.NewGuid():N}";
            if (payment is null)
            {
                payment = new Payment(orderId, buyerId, _gateway.Currency, amount, invoiceId, Now);
                payment.BeginAuthorization(requestId, invoiceId, amount, instrument.PaymentMethodId, lastDigits, Now);
                await _payments.AddAsync(payment, cancellationToken);
            }
            else
            {
                payment.BeginAuthorization(requestId, invoiceId, amount, instrument.PaymentMethodId, lastDigits, Now);
                await _payments.UpdateAsync(payment, cancellationToken);
            }
        }
        // else: an earlier attempt's outcome is unknown — replay its request id; the provider returns
        // the original result instead of placing a second hold.

        var command = new AuthorizePaymentCommand(
            orderId,
            payment.AuthorizationRequestId!,
            payment.Amount,
            payment.InvoiceId,
            CustomId: $"eshop-order-{orderId}",
            Description: $"eShopOnWeb order {orderId}",
            instrument.Card,
            savedCardToken);

        ProviderAuthorization authorization;
        try
        {
            authorization = await _gateway.AuthorizeAsync(command, cancellationToken);
        }
        catch (PaymentProviderException ex) when (ex.IsUnknownOutcome)
        {
            _logger.LogWarning("Authorization for order {OrderId} (request {RequestId}) has an unknown outcome; replaying the same request id to settle it.", orderId, command.RequestId);
            try
            {
                authorization = await _gateway.AuthorizeAsync(command, cancellationToken);
            }
            catch (PaymentProviderException again) when (again.IsUnknownOutcome)
            {
                _logger.LogError(again, "Authorization for order {OrderId} (request {RequestId}) is still unsettled; left as AuthorizationPending.", orderId, command.RequestId);
                throw new PaymentProviderException(PaymentProviderErrorKind.Timeout,
                    "PayPal did not respond, so it is not yet known whether the card was authorized. " +
                    "Retry the same request: it is replayed safely and will never authorize twice.",
                    innerException: again);
            }
            catch (PaymentProviderException rejected)
            {
                await FailAuthorizationAsync(payment, rejected, cancellationToken);
                throw;
            }
        }
        catch (PaymentProviderException ex)
        {
            await FailAuthorizationAsync(payment, ex, cancellationToken);
            throw;
        }

        var held = payment.RecordAuthorization(authorization, Now);
        if (held)
        {
            order.MarkPaymentAuthorized();
        }
        await _payments.UpdateAsync(payment, cancellationToken);
        await _orders.UpdateAsync(order, cancellationToken);

        if (!held)
        {
            _logger.LogWarning("Authorization for order {OrderId} not approved: {Reason}", orderId, payment.LastError ?? "");
            if (authorization.Outcome is AuthorizationOutcome.Approved or AuthorizationOutcome.Pending)
            {
                await ReleaseMismatchedHoldAsync(orderId, authorization.AuthorizationId, cancellationToken);
            }
            throw new PaymentProviderException(PaymentProviderErrorKind.Rejected,
                $"The payment was not authorized: {payment.LastError}");
        }

        _logger.LogInformation("Order {OrderId} authorized: PayPal order {ProviderOrderId}, authorization {AuthorizationId}, {Amount} {Currency}.",
            orderId, payment.ProviderOrderId ?? "", payment.AuthorizationId ?? "", payment.Amount, payment.Currency);
        return new PaymentOperationResult(order, payment, AlreadyDone: false);
    }

    private async Task FailAuthorizationAsync(Payment payment, PaymentProviderException ex, CancellationToken cancellationToken)
    {
        payment.RecordAuthorizationFailure(Describe(ex), Now);
        await _payments.UpdateAsync(payment, CancellationToken.None);
        _logger.LogWarning("Authorization for order {OrderId} failed: {Kind} {Message} (debug_id {DebugId})",
            payment.OrderId, ex.Kind, ex.Message, ex.DebugId ?? "");
    }

    private async Task ReleaseMismatchedHoldAsync(int orderId, string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            await _gateway.VoidAsync(authorizationId, $"eshop-{orderId}-void-{authorizationId}", cancellationToken);
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogError(ex, "Could not release mismatched authorization {AuthorizationId} for order {OrderId}.", authorizationId, orderId);
        }
    }

    // ------------------------------------------------------------------ capture

    public async Task<PaymentOperationResult> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        await using var orderLock = await OrderPaymentLock.AcquireAsync(_claims, orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = await _payments.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        switch (order.Status)
        {
            case OrderStatus.Fulfilled:
                await RefreshCaptureFeesAsync(payment!, cancellationToken);
                return new PaymentOperationResult(order, payment, AlreadyDone: true);
            case OrderStatus.Cancelled:
                throw new PaymentConflictException($"Order {orderId} is cancelled and cannot be fulfilled.", "ORDER_CANCELLED");
            case OrderStatus.AwaitingPayment:
                throw new PaymentConflictException(
                    $"Order {orderId} has no authorized payment yet (payment status: {payment?.Status.ToString() ?? "none"}). The shopper must pay before it can be fulfilled.",
                    "NOT_AUTHORIZED");
        }

        if (payment is null)
            throw new InvalidOperationException($"Order {orderId} is authorized but has no payment record.");

        if (payment.Status != PaymentStatus.CapturePending)
        {
            var settled = await EnsureHoldCapturableAsync(order, payment, cancellationToken);
            if (settled is not null)
            {
                return await CompleteFulfilmentAsync(order, payment, settled, cancellationToken);
            }
            payment.BeginCapture($"eshop-{orderId}-capture-{payment.AuthorizationId}", Now);
            await _payments.UpdateAsync(payment, cancellationToken);
        }
        else if (payment.ProviderOrderId is not null)
        {
            // An earlier capture's outcome was unknown: look before replaying.
            var found = await TryFindCaptureAsync(payment, cancellationToken);
            if (found is not null)
            {
                return await CompleteFulfilmentAsync(order, payment, found, cancellationToken);
            }
        }

        ProviderCapture capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, payment.Amount, payment.CaptureRequestId!, cancellationToken);
        }
        catch (PaymentProviderException ex) when (ex.IsUnknownOutcome)
        {
            _logger.LogWarning("Capture for order {OrderId} (request {RequestId}) has an unknown outcome; re-reading PayPal order {ProviderOrderId}.",
                orderId, payment.CaptureRequestId ?? "", payment.ProviderOrderId ?? "");
            var found = await TryFindCaptureAsync(payment, cancellationToken);
            if (found is null)
            {
                _logger.LogError(ex, "Capture for order {OrderId} is still unsettled; left as CapturePending.", orderId);
                throw new PaymentProviderException(PaymentProviderErrorKind.Timeout,
                    "PayPal did not respond, so it is not yet known whether the payment was captured. " +
                    "Retry the fulfilment: it checks PayPal first and never captures twice.",
                    innerException: ex);
            }
            capture = found;
        }
        catch (PaymentProviderException ex)
        {
            payment.RevertCapture(Describe(ex), Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            await ThrowIfHoldLostAsync(order, payment, cancellationToken);
            throw new PaymentProviderException(ex.Kind,
                $"PayPal refused to capture authorization {payment.AuthorizationId}: {ex.Message} No money was taken; the hold is unchanged.",
                ex.ProviderStatusCode, ex.ProviderErrorName, ex.DebugId, ex.Issues, ex);
        }

        if (capture.Outcome == CaptureOutcome.Failed)
        {
            payment.RevertCapture($"PayPal reported the capture as {capture.ProviderStatus}.", Now);
            await _payments.UpdateAsync(payment, cancellationToken);
            throw new PaymentProviderException(PaymentProviderErrorKind.Rejected,
                $"PayPal reported capture {capture.CaptureId} as {capture.ProviderStatus}; no money was taken.");
        }

        return await CompleteFulfilmentAsync(order, payment, capture, cancellationToken);
    }

    private async Task<PaymentOperationResult> CompleteFulfilmentAsync(Order order, Payment payment, ProviderCapture capture, CancellationToken cancellationToken)
    {
        if (payment.Status == PaymentStatus.Authorized)
        {
            payment.BeginCapture(payment.CaptureRequestId ?? $"eshop-{order.Id}-capture-{payment.AuthorizationId}", Now);
        }
        payment.RecordCapture(capture, Now);
        order.MarkFulfilled();
        await _payments.UpdateAsync(payment, cancellationToken);
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} fulfilled: capture {CaptureId} {Status}, gross {Gross}, fee {Fee}, net {Net} {Currency}.",
            order.Id, capture.CaptureId, capture.ProviderStatus, payment.CapturedAmount ?? 0m, payment.PayPalFee ?? 0m, payment.NetAmount ?? 0m, payment.Currency);
        return new PaymentOperationResult(order, payment, AlreadyDone: false);
    }

    /// <summary>
    /// Makes sure the hold can still be captured, renewing it once the honor period has passed.
    /// Returns a capture when the provider shows the authorization was already captured.
    /// </summary>
    private async Task<ProviderCapture?> EnsureHoldCapturableAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        var state = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
        payment.UpdateAuthorizationState(state, Now);

        switch (state.Outcome)
        {
            case AuthorizationOutcome.Captured:
                var existing = await TryFindCaptureAsync(payment, cancellationToken);
                if (existing is not null)
                    return existing;
                throw new PaymentConflictException(
                    $"PayPal shows authorization {payment.AuthorizationId} as captured, but the capture could not be read back. Retry the fulfilment shortly.",
                    "CAPTURE_NOT_VISIBLE");
            case AuthorizationOutcome.Voided:
            case AuthorizationOutcome.Denied:
            case AuthorizationOutcome.NotCapturable:
                await HoldLostAsync(order, payment, $"is {state.ProviderStatus} at PayPal", cancellationToken);
                break;
        }

        var now = Now;
        var originalPlacedAt = payment.AuthorizedAt ?? state.CreatedAt ?? now;
        var expiresAt = payment.AuthorizationExpiresAt ?? originalPlacedAt + AuthorizationPeriod;
        if (now >= expiresAt)
        {
            await HoldLostAsync(order, payment, $"expired on {expiresAt:yyyy-MM-dd HH:mm} UTC", cancellationToken);
        }

        var holdPlacedAt = payment.CurrentHoldPlacedAt ?? originalPlacedAt;
        if (now - holdPlacedAt >= HonorPeriod)
        {
            await RenewHoldAsync(order, payment, expiresAt, cancellationToken);
        }

        await _payments.UpdateAsync(payment, cancellationToken);
        return null;
    }

    private async Task RenewHoldAsync(Order order, Payment payment, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var authorizationId = payment.AuthorizationId!;
        var requestId = $"eshop-{order.Id}-reauth-{authorizationId}";
        ProviderAuthorizationState renewed;
        try
        {
            renewed = await _gateway.ReauthorizeAsync(authorizationId, payment.Amount, requestId, cancellationToken);
        }
        catch (PaymentProviderException ex) when (ex.IsUnknownOutcome)
        {
            try
            {
                renewed = await _gateway.ReauthorizeAsync(authorizationId, payment.Amount, requestId, cancellationToken);
            }
            catch (PaymentProviderException again) when (again.IsUnknownOutcome)
            {
                throw new PaymentProviderException(PaymentProviderErrorKind.Timeout,
                    "PayPal did not respond while renewing the stale authorization. Retry the fulfilment: the renewal is replayed safely.",
                    innerException: again);
            }
            catch (PaymentProviderException rejected)
            {
                LogRenewalRefused(order, authorizationId, expiresAt, rejected);
                return;
            }
        }
        catch (PaymentProviderException ex) when (ex.Kind == PaymentProviderErrorKind.Rejected)
        {
            LogRenewalRefused(order, authorizationId, expiresAt, ex);
            return;
        }

        payment.RecordReauthorization(renewed, Now);
        await _payments.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Order {OrderId}: stale authorization {OldAuthorizationId} renewed as {AuthorizationId}.",
            order.Id, authorizationId, renewed.AuthorizationId);
    }

    private void LogRenewalRefused(Order order, string authorizationId, DateTimeOffset expiresAt, PaymentProviderException ex) =>
        _logger.LogWarning("Order {OrderId}: PayPal refused to renew authorization {AuthorizationId} ({Message}, debug_id {DebugId}); capturing the original, valid until {ExpiresAt}.",
            order.Id, authorizationId, ex.Message, ex.DebugId ?? "", expiresAt);

    private async Task ThrowIfHoldLostAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        ProviderAuthorizationState state;
        try
        {
            state = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
        }
        catch (PaymentProviderException)
        {
            return; // the original refusal is reported instead
        }

        if (state.Outcome is AuthorizationOutcome.Voided or AuthorizationOutcome.Denied or AuthorizationOutcome.NotCapturable)
        {
            payment.UpdateAuthorizationState(state, Now);
            await HoldLostAsync(order, payment, $"is {state.ProviderStatus} at PayPal", cancellationToken);
        }
    }

    /// <summary>The hold can neither be captured nor renewed: say so in terms an operator can act on.</summary>
    private async Task HoldLostAsync(Order order, Payment payment, string why, CancellationToken cancellationToken)
    {
        var authorizationId = payment.AuthorizationId;
        payment.MarkAuthorizationExpired($"Authorization {authorizationId} {why}; it can no longer be captured or renewed.", Now);
        order.ReturnToAwaitingPayment();
        await _payments.UpdateAsync(payment, CancellationToken.None);
        await _orders.UpdateAsync(order, CancellationToken.None);
        _logger.LogWarning("Order {OrderId}: authorization {AuthorizationId} {Why}; order returned to AwaitingPayment.", order.Id, authorizationId ?? "", why);
        throw new PaymentConflictException(
            $"The payment authorization for order {order.Id} ({authorizationId}) {why} and can no longer be captured or renewed. " +
            $"No money was taken. The order is back to AwaitingPayment: ask the shopper to pay again (POST /api/orders/{order.Id}/pay), " +
            $"or cancel the order (POST /api/orders/{order.Id}/cancel).",
            "AUTHORIZATION_EXPIRED");
    }

    private async Task<ProviderCapture?> TryFindCaptureAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (payment.ProviderOrderId is null)
            return null;
        try
        {
            return await _gateway.FindCaptureAsync(payment.ProviderOrderId, cancellationToken);
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning("Could not re-read PayPal order {ProviderOrderId}: {Message}", payment.ProviderOrderId, ex.Message);
            return null;
        }
    }

    /// <summary>A capture reported PENDING carries no fee yet; re-read it so the payment shows what PayPal reported.</summary>
    private async Task RefreshCaptureFeesAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (payment.CaptureId is null || payment.PayPalFee is not null)
            return;
        try
        {
            var capture = await _gateway.GetCaptureAsync(payment.CaptureId, cancellationToken);
            payment.RecordCapture(capture, Now);
            await _payments.UpdateAsync(payment, cancellationToken);
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning("Could not refresh capture {CaptureId}: {Message}", payment.CaptureId, ex.Message);
        }
    }

    // ------------------------------------------------------------------ void

    public async Task<PaymentOperationResult> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        await using var orderLock = await OrderPaymentLock.AcquireAsync(_claims, orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = await _payments.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        switch (order.Status)
        {
            case OrderStatus.Cancelled:
                return new PaymentOperationResult(order, payment, AlreadyDone: true);
            case OrderStatus.Fulfilled:
                throw new PaymentConflictException(
                    $"Order {orderId} is fulfilled and its payment was captured; return the money with POST /api/orders/{orderId}/refunds instead.",
                    "ORDER_FULFILLED");
            case OrderStatus.AwaitingPayment:
                if (payment is { Status: PaymentStatus.AuthorizationPending })
                    throw new PaymentConflictException(
                        $"The outcome of the last payment attempt for order {orderId} is still being verified with PayPal; " +
                        "retry the payment request to settle it, then cancel.",
                        "PAYMENT_OUTCOME_UNKNOWN");
                order.MarkCancelled();
                await _orders.UpdateAsync(order, cancellationToken);
                _logger.LogInformation("Order {OrderId} cancelled before payment; no money moved.", orderId);
                return new PaymentOperationResult(order, payment, AlreadyDone: false);
        }

        if (payment is null)
            throw new InvalidOperationException($"Order {orderId} is authorized but has no payment record.");

        var authorizationId = payment.AuthorizationId!;
        if (payment.Status == PaymentStatus.VoidPending)
        {
            var current = await TryGetAuthorizationAsync(authorizationId, cancellationToken);
            if (current is { Outcome: AuthorizationOutcome.Voided or AuthorizationOutcome.NotCapturable or AuthorizationOutcome.Denied })
                return await CompleteCancellationAsync(order, payment, current, cancellationToken);
        }
        else
        {
            payment.BeginVoid($"eshop-{orderId}-void-{authorizationId}", Now);
            await _payments.UpdateAsync(payment, cancellationToken);
        }

        ProviderAuthorizationState voided;
        try
        {
            voided = await _gateway.VoidAsync(authorizationId, payment.VoidRequestId!, cancellationToken);
        }
        catch (PaymentProviderException ex) when (ex.IsUnknownOutcome)
        {
            var current = await TryGetAuthorizationAsync(authorizationId, cancellationToken);
            if (current is not { Outcome: AuthorizationOutcome.Voided })
            {
                _logger.LogError(ex, "Void for order {OrderId} is still unsettled; left as VoidPending.", orderId);
                throw new PaymentProviderException(PaymentProviderErrorKind.Timeout,
                    "PayPal did not respond, so it is not yet known whether the held funds were released. Retry the cancellation: it checks PayPal first.",
                    innerException: ex);
            }
            voided = current;
        }
        catch (PaymentProviderException ex)
        {
            var current = await TryGetAuthorizationAsync(authorizationId, cancellationToken);
            if (current is { Outcome: AuthorizationOutcome.Voided or AuthorizationOutcome.NotCapturable or AuthorizationOutcome.Denied })
            {
                // The hold is already gone at PayPal: nothing left to release.
                return await CompleteCancellationAsync(order, payment, current, cancellationToken);
            }
            payment.RevertVoid(Describe(ex), Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            throw new PaymentProviderException(ex.Kind,
                $"PayPal refused to release authorization {authorizationId}: {ex.Message}",
                ex.ProviderStatusCode, ex.ProviderErrorName, ex.DebugId, ex.Issues, ex);
        }

        if (voided.Outcome != AuthorizationOutcome.Voided)
        {
            // Minimal/odd response: confirm from PayPal's own record.
            voided = await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
            if (voided.Outcome is not (AuthorizationOutcome.Voided or AuthorizationOutcome.NotCapturable))
            {
                payment.RevertVoid($"PayPal reports authorization {authorizationId} as {voided.ProviderStatus} after the void.", Now);
                await _payments.UpdateAsync(payment, CancellationToken.None);
                throw new PaymentProviderException(PaymentProviderErrorKind.Rejected,
                    $"PayPal reports authorization {authorizationId} as {voided.ProviderStatus}; the held funds were not released.");
            }
        }

        return await CompleteCancellationAsync(order, payment, voided, cancellationToken);
    }

    private async Task<PaymentOperationResult> CompleteCancellationAsync(Order order, Payment payment, ProviderAuthorizationState state, CancellationToken cancellationToken)
    {
        if (payment.Status == PaymentStatus.Authorized)
            payment.BeginVoid(payment.VoidRequestId ?? $"eshop-{order.Id}-void-{payment.AuthorizationId}", Now);
        payment.RecordVoid(state, Now);
        order.MarkCancelled();
        await _payments.UpdateAsync(payment, cancellationToken);
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} cancelled; authorization {AuthorizationId} released ({Status}).",
            order.Id, payment.AuthorizationId ?? "", state.ProviderStatus);
        return new PaymentOperationResult(order, payment, AlreadyDone: false);
    }

    private async Task<ProviderAuthorizationState?> TryGetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning("Could not re-read authorization {AuthorizationId}: {Message}", authorizationId, ex.Message);
            return null;
        }
    }

    // ------------------------------------------------------------------ refund

    public async Task<RefundOperationResult> RefundAsync(string buyerId, int orderId, decimal? amount, string? idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || !IdempotencyKeyPattern.IsMatch(idempotencyKey))
            throw new PaymentValidationException("An idempotencyKey (1-64 characters: letters, digits, '_', '-', '.', ':') is required for refunds.");
        if (amount is <= 0)
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        if (amount is { } requested)
            CurrencyRules.EnsureRepresentable(requested, _gateway.Currency, "Refund amount");

        await using var orderLock = await OrderPaymentLock.AcquireAsync(_claims, orderId, cancellationToken);
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _payments.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        if (order.Status != OrderStatus.Fulfilled || payment is null || !payment.HasCapture)
            throw new PaymentConflictException(
                order.Status == OrderStatus.PaymentAuthorized
                    ? $"Order {orderId} has not been fulfilled, so no money was taken yet; cancel the order to release the hold instead."
                    : $"Order {orderId} has no captured payment to refund (order status {order.Status}).",
                "NOT_CAPTURED");

        // The key's claim: the store refuses a second claim of the same key, so one key = one refund.
        var claim = await _claims.TryAcquireAsync($"refund:{orderId}:{idempotencyKey}", TimeSpan.MaxValue, cancellationToken);
        if (claim is null)
        {
            var existing = payment.FindRefund(idempotencyKey)
                ?? throw new PaymentConflictException($"A refund with idempotency key '{idempotencyKey}' is being processed. Retry shortly.", "OPERATION_IN_PROGRESS");
            if (amount is { } a && a != existing.Amount)
                throw new PaymentConflictException(
                    $"Idempotency key '{idempotencyKey}' was already used for a refund of {existing.Amount} {payment.Currency}; use a new key for a different refund.",
                    "IDEMPOTENCY_KEY_REUSED");
            if (existing.Status == PaymentRefundStatus.Pending && existing.ProviderRefundId is null)
            {
                await SendRefundAsync(order, payment, existing, cancellationToken);
            }
            return new RefundOperationResult(order, payment, existing, Replayed: true);
        }

        PaymentRefund refund;
        try
        {
            var refundAmount = amount ?? payment.RefundableAmount;
            if (refundAmount <= 0)
                throw new PaymentConflictException($"Order {orderId} has already been fully refunded.", "NOTHING_TO_REFUND");
            refund = payment.AddRefund(idempotencyKey, refundAmount, $"eshop-{orderId}-refund-{Guid.NewGuid():N}", Now);
            await _payments.UpdateAsync(payment, cancellationToken);
        }
        catch
        {
            await _claims.ReleaseAsync(claim, CancellationToken.None);
            throw;
        }

        await SendRefundAsync(order, payment, refund, cancellationToken);
        return new RefundOperationResult(order, payment, refund, Replayed: false);
    }

    private async Task SendRefundAsync(Order order, Payment payment, PaymentRefund refund, CancellationToken cancellationToken)
    {
        var customId = $"eshop-refund-{order.Id}-{refund.Id}";
        ProviderRefund result;
        try
        {
            result = await _gateway.RefundAsync(payment.CaptureId!, refund.Amount, refund.ProviderRequestId, customId, cancellationToken);
        }
        catch (PaymentProviderException ex) when (ex.IsUnknownOutcome)
        {
            _logger.LogWarning("Refund {RefundId} for order {OrderId} has an unknown outcome; replaying request {RequestId}.", refund.Id, order.Id, refund.ProviderRequestId);
            try
            {
                result = await _gateway.RefundAsync(payment.CaptureId!, refund.Amount, refund.ProviderRequestId, customId, cancellationToken);
            }
            catch (PaymentProviderException again) when (again.IsUnknownOutcome)
            {
                _logger.LogError(again, "Refund {RefundId} for order {OrderId} is still unsettled; left Pending.", refund.Id, order.Id);
                throw new PaymentProviderException(PaymentProviderErrorKind.Timeout,
                    "PayPal did not respond, so it is not yet known whether the refund went through. " +
                    "Retry with the same idempotency key: it is replayed safely and never refunds twice.",
                    innerException: again);
            }
            catch (PaymentProviderException rejected)
            {
                await FailRefundAsync(order, payment, refund, rejected);
                throw;
            }
        }
        catch (PaymentProviderException ex)
        {
            await FailRefundAsync(order, payment, refund, ex);
            throw;
        }

        payment.RecordRefund(refund, result, Now);
        await _payments.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Order {OrderId}: refund {RefundId} of {Amount} {Currency} is {Status} (PayPal refund {ProviderRefundId}).",
            order.Id, refund.Id, refund.Amount, payment.Currency, result.ProviderStatus, result.RefundId);
    }

    private async Task FailRefundAsync(Order order, Payment payment, PaymentRefund refund, PaymentProviderException ex)
    {
        payment.FailRefund(refund, Describe(ex), Now);
        await _payments.UpdateAsync(payment, CancellationToken.None);
        _logger.LogWarning("Order {OrderId}: refund {RefundId} refused by PayPal: {Message} (debug_id {DebugId})", order.Id, refund.Id, ex.Message, ex.DebugId ?? "");
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
        ?? throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");

    /// <summary>Another shopper's order is reported exactly like a missing one.</summary>
    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        return order;
    }

    private static string Describe(PaymentProviderException ex)
    {
        var issues = ex.Issues.Count > 0 ? $" [{string.Join(", ", ex.Issues)}]" : "";
        var debug = ex.DebugId is null ? "" : $" (PayPal debug_id {ex.DebugId})";
        return $"{ex.Message}{issues}{debug}";
    }
}
