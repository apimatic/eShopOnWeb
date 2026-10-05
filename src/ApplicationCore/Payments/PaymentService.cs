using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>What the shopper pays with: one-off card details, or one of their saved cards.</summary>
public record PayOrderCommand(CardDetails? Card, int? PaymentMethodId);

/// <summary>
/// Moves the money behind an order: authorize at checkout, capture at fulfilment, void on cancel, refund on return.
/// Every PayPal write follows claim → call → record, so a double click or a concurrent caller is refused by the
/// claim store before it reaches PayPal, and every write is (re)sent under one stored PayPal-Request-Id.
/// </summary>
public class PaymentService
{
    private static readonly Regex IdempotencyKeyPattern = new("^[A-Za-z0-9._:-]{1,64}$", RegexOptions.Compiled);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderPayment> _payments;
    private readonly IRepository<SavedPaymentMethod> _paymentMethods;
    private readonly IPaymentClaimStore _claims;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentSettings _settings;
    private readonly TimeProvider _clock;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(IRepository<Order> orders,
        IRepository<OrderPayment> payments,
        IRepository<SavedPaymentMethod> paymentMethods,
        IPaymentClaimStore claims,
        IPaymentGateway gateway,
        PaymentSettings settings,
        TimeProvider clock,
        IAppLogger<PaymentService> logger)
    {
        _orders = orders;
        _payments = payments;
        _paymentMethods = paymentMethods;
        _claims = claims;
        _gateway = gateway;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken ct) =>
        _payments.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

    public async Task<(Order? Order, OrderPayment? Payment)> GetOrderWithPaymentAsync(int orderId, CancellationToken ct) =>
        (await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct), await GetPaymentAsync(orderId, ct));

    /// <summary>The caller's own orders, newest first, each with its payment (if any).</summary>
    public async Task<System.Collections.Generic.IReadOnlyList<(Order Order, OrderPayment? Payment)>> ListOrdersForBuyerAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _payments.ListAsync(new PaymentsByOrderIdsSpec(orders.Select(o => o.Id).ToArray()), ct);
        return orders.OrderByDescending(o => o.Id)
            .Select(o => (o, payments.FirstOrDefault(p => p.OrderId == o.Id)))
            .ToList();
    }

    // ───────────────────────────── Pay (authorize) ─────────────────────────────

    public async Task<OrderPayment> PayAsync(int orderId, string buyerId, PayOrderCommand command, CancellationToken ct)
    {
        if ((command.Card is null) == (command.PaymentMethodId is null))
            throw new PaymentRequestException(PaymentErrorKind.Validation, "payment_source_required",
                "Provide either card details or a saved paymentMethodId — exactly one.");

        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);
        if (order.Status == OrderStatus.Cancelled)
            throw new PaymentRequestException(PaymentErrorKind.Conflict, "order_cancelled", $"Order {orderId} was cancelled.");

        var payment = await GetPaymentAsync(orderId, ct);
        if (payment is not null && payment.Status is not (PaymentStatus.AuthorizationFailed or PaymentStatus.Authorizing))
            return payment; // already paid: a repeated click gets the same outcome, not a second hold

        var source = await ResolveSourceAsync(command, buyerId, ct);
        if (payment is { Status: PaymentStatus.Authorizing })
        {
            if (!NeedsSettlement(payment))
                throw InProgress("A payment for this order is already being processed.");
            return await ContinueAuthorizationAsync(payment, order, source, ct);
        }

        var amount = order.Total();
        if (!MoneyFormat.IsExact(amount, _settings.Currency))
            throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "amount_precision",
                $"The order total {amount} cannot be charged in {_settings.Currency}.");

        var attempt = (payment?.Attempt ?? 0) + 1;
        var claimKey = $"authorize:{orderId}:{attempt}";
        if (!await ClaimAsync(claimKey, () => payment is null || payment.Attempt < attempt, ct))
            throw InProgress("A payment for this order is already being processed.");

        if (payment is null)
        {
            payment = new OrderPayment(orderId, buyerId, amount, _settings.Currency, Now);
            payment.StartAuthorization(NewInvoiceId(orderId), NewRequestId(), NewRequestId(), command.PaymentMethodId, Now);
            await _payments.AddAsync(payment, ct);
        }
        else
        {
            payment.StartAuthorization(NewInvoiceId(orderId), NewRequestId(), NewRequestId(), command.PaymentMethodId, Now);
            await _payments.UpdateAsync(payment, ct);
        }

        return await ContinueAuthorizationAsync(payment, order, source, ct);
    }

    /// <summary>
    /// Drives an Authorizing payment forward. Safe to repeat: the PayPal order is created under its stored
    /// request id, and an existing authorization is read back rather than placed twice.
    /// </summary>
    private async Task<OrderPayment> ContinueAuthorizationAsync(OrderPayment payment, Order order, PaymentSourceInput source, CancellationToken ct)
    {
        try
        {
            if (payment.PayPalOrderId is null)
            {
                var created = await WithOneResendOnUnknownAsync(() => _gateway.CreateOrderAsync(new CreateGatewayOrder(
                    payment.Amount, payment.Currency, CustomId(order.Id), payment.InvoiceId!,
                    $"eShop order {order.Id}", payment.CreateOrderRequestId!), ct));
                payment.PayPalOrderCreated(created.Id, Now);
                await _payments.UpdateAsync(payment, ct);
            }
            else if (payment.OutcomeUnknownSince is not null || NeedsSettlement(payment))
            {
                // An earlier authorize may have landed: read the order back before authorizing again.
                var existing = await _gateway.GetOrderAsync(payment.PayPalOrderId, ct);
                if (existing.Authorization is not null)
                {
                    return await ApplyAuthorizationAsync(payment, order, existing, ct);
                }
            }

            GatewayOrderAuthorization result;
            try
            {
                result = await _gateway.AuthorizeOrderAsync(payment.PayPalOrderId!, source, payment.AuthorizeRequestId!, ct);
            }
            catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)
            {
                // Settle here: the hold may exist even though the call failed.
                result = await _gateway.GetOrderAsync(payment.PayPalOrderId!, ct);
                if (result.Authorization is null)
                {
                    throw;
                }
            }

            return await ApplyAuthorizationAsync(payment, order, result, ct);
        }
        catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)
        {
            payment.MarkOutcomeUnknown("PayPal did not confirm the authorization; it will be settled automatically.", Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            throw new PaymentOutcomePendingException(
                "PayPal did not respond in time, so it is not yet known whether the card was authorized. Repeat the same request to settle it; you will not be charged twice.", ex);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning("Authorization for order {OrderId} refused by PayPal: {Name} {Issue} (debug id {DebugId})",
                order.Id, ex.ProviderErrorName ?? "-", ex.ProviderIssue ?? "-", ex.DebugId ?? "-");
            payment.AuthorizationFailed(DescribeRefusal("PayPal refused the payment", ex), Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            throw;
        }
    }

    private async Task<OrderPayment> ApplyAuthorizationAsync(OrderPayment payment, Order order, GatewayOrderAuthorization result, CancellationToken ct)
    {
        if (string.Equals(result.OrderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            payment.AuthorizationFailed("PayPal requires the shopper to complete card authentication (3-D Secure) in a browser, which this checkout does not support.", Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "payer_action_required", payment.LastError!);
        }

        var auth = result.Authorization;
        if (auth is null || string.Equals(auth.Status, "DENIED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(auth.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            payment.AuthorizationFailed(auth is null
                ? $"PayPal did not authorize the payment (order status {result.OrderStatus ?? "unknown"})."
                : $"The card was declined (authorization status {auth.Status}).", Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "payment_declined", payment.LastError!);
        }

        if (auth.Amount != payment.Amount || !string.Equals(auth.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            // Never keep a hold that differs from the order total.
            _logger.LogWarning("Authorization {AuthorizationId} for order {OrderId} is {Amount} {Currency}, expected {Expected} {ExpectedCurrency}; voiding it.",
                auth.Id, order.Id, auth.Amount?.ToString() ?? "?", auth.Currency ?? "?", payment.Amount, payment.Currency);
            try { await _gateway.VoidAsync(auth.Id, NewRequestId(), ct); }
            catch (PaymentGatewayException ex) { _logger.LogWarning("Voiding mismatched authorization {AuthorizationId} failed: {Message}", auth.Id, ex.Message); }
            payment.AuthorizationFailed("PayPal authorized an amount different from the order total; the hold was released.", Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "amount_mismatch", payment.LastError!);
        }

        payment.Authorized(auth.Id, auth.Status, auth.CreatedAt, auth.ExpiresAt, result.CardBrand, result.CardLastDigits, Now);
        await _payments.UpdateAsync(payment, CancellationToken.None);
        if (order.Status == OrderStatus.AwaitingPayment)
        {
            order.MarkPaymentAuthorized();
            await _orders.UpdateAsync(order, CancellationToken.None);
        }
        return payment;
    }

    private async Task<PaymentSourceInput> ResolveSourceAsync(PayOrderCommand command, string buyerId, CancellationToken ct)
    {
        if (command.PaymentMethodId is int methodId)
        {
            // A saved card is only usable by the shopper who saved it, and only while it is active.
            var method = await _paymentMethods.FirstOrDefaultAsync(new SavedPaymentMethodForBuyerSpec(methodId, buyerId), ct);
            if (method is null || !method.IsUsable)
                throw PaymentRequestException.NotFound("Saved payment method");
            return PaymentSourceInput.FromVault(method.PayPalVaultId!);
        }

        CardValidation.Validate(command.Card!, Now);
        return PaymentSourceInput.FromCard(command.Card!);
    }

    // ───────────────────────────── Fulfil (capture) ─────────────────────────────

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        var payment = await GetPaymentAsync(orderId, ct);
        if (payment is null || payment.Status is PaymentStatus.AuthorizationFailed or PaymentStatus.Authorizing)
            throw new PaymentRequestException(PaymentErrorKind.Conflict, "not_authorized", $"Order {orderId} has no authorized payment to capture.");

        switch (payment.Status)
        {
            case PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded:
                await EnsureOrderStatusAsync(order, OrderStatus.Fulfilled, ct);
                return payment;
            case PaymentStatus.Voiding or PaymentStatus.Voided:
                throw new PaymentRequestException(PaymentErrorKind.Conflict, "order_cancelled", $"Order {orderId} was cancelled; its payment was released.");
            case PaymentStatus.Capturing or PaymentStatus.Reauthorizing:
                if (!NeedsSettlement(payment)) throw InProgress("This order is already being fulfilled.");
                return await ContinueFulfilmentAsync(payment, order, ct);
        }

        if (!await ClaimAsync(SettleClaimKey(payment), () => payment.Status == PaymentStatus.Authorized, ct))
            throw InProgress("This order is already being fulfilled or cancelled.");

        return await ContinueFulfilmentAsync(payment, order, ct);
    }

    private async Task<OrderPayment> ContinueFulfilmentAsync(OrderPayment payment, Order order, CancellationToken ct)
    {
        try
        {
            if (payment.Status == PaymentStatus.Authorized)
            {
                var originalHold = payment.FirstAuthorizedAt ?? payment.AuthorizedAt ?? payment.CreatedAt;
                var expiresAt = payment.AuthorizationExpiresAt ?? originalHold + _settings.MaximumAuthorizationAge;
                if (Now >= expiresAt)
                {
                    var message = $"The payment authorization for order {order.Id} expired on {expiresAt:u} and can no longer be renewed or captured. " +
                                  "Cancel this order and ask the shopper to place and pay for a new one.";
                    await FailSettleActionAsync(payment, message, ct);
                    throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "authorization_expired", message);
                }

                if (Now >= (payment.AuthorizedAt ?? originalHold) + _settings.HonorPeriod)
                {
                    payment.StartReauthorization(NewRequestId(), Now);
                    await _payments.UpdateAsync(payment, ct);
                }
            }

            if (payment.Status == PaymentStatus.Reauthorizing)
            {
                await RenewAuthorizationAsync(payment, order, ct);
            }

            if (payment.Status == PaymentStatus.Authorized)
            {
                payment.StartCapture(NewRequestId(), Now);
                await _payments.UpdateAsync(payment, ct);
            }

            GatewayCapture capture;
            try
            {
                capture = await WithOneResendOnUnknownAsync(() => _gateway.CaptureAsync(payment.AuthorizationId!, payment.Amount, payment.Currency,
                    payment.InvoiceId, payment.CaptureRequestId!, ct));
            }
            catch (PaymentGatewayException ex) when (!ex.OutcomeUnknown)
            {
                _logger.LogWarning("Capture for order {OrderId} refused by PayPal: {Name} {Issue} (debug id {DebugId})",
                    order.Id, ex.ProviderErrorName ?? "-", ex.ProviderIssue ?? "-", ex.DebugId ?? "-");
                var message = DescribeRefusal("PayPal refused to capture the payment", ex) +
                              " The hold is unchanged; fix the cause and fulfil again, or cancel the order to release the funds.";
                await FailSettleActionAsync(payment, message, ct);
                throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "capture_refused", message);
            }

            if (string.Equals(capture.Status, "DECLINED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(capture.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                var message = $"PayPal reported the capture as {capture.Status}. No money was taken; cancel the order or ask the shopper to pay again.";
                await FailSettleActionAsync(payment, message, ct);
                throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "capture_declined", message);
            }

            payment.Captured(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.Net, Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            await EnsureOrderStatusAsync(order, OrderStatus.Fulfilled, CancellationToken.None);
            return payment;
        }
        catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)
        {
            payment.MarkOutcomeUnknown("PayPal did not confirm the capture; it will be settled automatically.", Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            throw new PaymentOutcomePendingException(
                "PayPal did not respond in time, so it is not yet known whether the payment was captured. Repeat the fulfilment to settle it; the shopper will not be charged twice.", ex);
        }
    }

    private async Task RenewAuthorizationAsync(OrderPayment payment, Order order, CancellationToken ct)
    {
        try
        {
            var renewed = await WithOneResendOnUnknownAsync(() => _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.Currency,
                payment.ReauthorizeRequestId!, ct));
            payment.Reauthorized(renewed.Id, renewed.Status, renewed.CreatedAt, renewed.ExpiresAt, Now);
            await _payments.UpdateAsync(payment, ct);
            _logger.LogInformation("Renewed stale authorization for order {OrderId}: new authorization {AuthorizationId}", order.Id, renewed.Id);
        }
        catch (PaymentGatewayException ex) when (!ex.OutcomeUnknown)
        {
            _logger.LogWarning("Reauthorization for order {OrderId} refused by PayPal: {Name} {Issue} (debug id {DebugId})",
                order.Id, ex.ProviderErrorName ?? "-", ex.ProviderIssue ?? "-", ex.DebugId ?? "-");
            var message = DescribeRefusal($"The authorization for order {order.Id} is past its honor period and PayPal refused to renew it", ex) +
                          " The funds can no longer be captured: cancel this order and ask the shopper to pay again.";
            await FailSettleActionAsync(payment, message, ct);
            throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "reauthorization_refused", message);
        }
    }

    // ───────────────────────────── Cancel (void) ─────────────────────────────

    public async Task<(Order Order, OrderPayment? Payment)> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.Status == OrderStatus.Fulfilled)
            throw new PaymentRequestException(PaymentErrorKind.Conflict, "order_fulfilled",
                $"Order {orderId} is already fulfilled and its payment captured; issue a refund instead.");

        var payment = await GetPaymentAsync(orderId, ct);
        if (payment is null || payment.Status == PaymentStatus.AuthorizationFailed)
        {
            await EnsureOrderStatusAsync(order, OrderStatus.Cancelled, ct);
            return (order, payment);
        }

        switch (payment.Status)
        {
            case PaymentStatus.Voided:
                await EnsureOrderStatusAsync(order, OrderStatus.Cancelled, ct);
                return (order, payment);
            case PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded:
                throw new PaymentRequestException(PaymentErrorKind.Conflict, "order_fulfilled",
                    $"Order {orderId}'s payment was already captured; issue a refund instead.");
            case PaymentStatus.Authorizing:
                throw InProgress("A payment for this order is still being processed; try again shortly.");
            case PaymentStatus.Capturing or PaymentStatus.Reauthorizing:
                throw InProgress("This order is being fulfilled.");
            case PaymentStatus.Voiding:
                if (!NeedsSettlement(payment)) throw InProgress("This order is already being cancelled.");
                return (order, await ContinueVoidAsync(payment, order, ct));
        }

        if (!await ClaimAsync(SettleClaimKey(payment), () => payment.Status == PaymentStatus.Authorized, ct))
            throw InProgress("This order is already being fulfilled or cancelled.");

        payment.StartVoid(NewRequestId(), Now);
        await _payments.UpdateAsync(payment, ct);
        return (order, await ContinueVoidAsync(payment, order, ct));
    }

    private async Task<OrderPayment> ContinueVoidAsync(OrderPayment payment, Order order, CancellationToken ct)
    {
        try
        {
            GatewayAuthorization voided;
            try
            {
                voided = await _gateway.VoidAsync(payment.AuthorizationId!, payment.VoidRequestId!, ct);
            }
            catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)
            {
                // Settle here: re-read the authorization to learn whether the void landed.
                voided = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, ct);
                if (!string.Equals(voided.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
                {
                    throw;
                }
            }
            catch (PaymentGatewayException ex)
            {
                // A refusal may simply mean it is already voided.
                var current = await TryGetAuthorizationAsync(payment.AuthorizationId!, ct);
                if (!string.Equals(current?.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Void for order {OrderId} refused by PayPal: {Name} {Issue} (debug id {DebugId})",
                        order.Id, ex.ProviderErrorName ?? "-", ex.ProviderIssue ?? "-", ex.DebugId ?? "-");
                    var message = DescribeRefusal("PayPal refused to release the held funds", ex);
                    await FailSettleActionAsync(payment, message, ct, current?.Status);
                    throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "void_refused", message);
                }
                voided = current!;
            }

            payment.Voided(voided.Status, Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            await EnsureOrderStatusAsync(order, OrderStatus.Cancelled, CancellationToken.None);
            return payment;
        }
        catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)
        {
            payment.MarkOutcomeUnknown("PayPal did not confirm the release of the held funds; it will be settled automatically.", Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            throw new PaymentOutcomePendingException(
                "PayPal did not respond in time, so it is not yet known whether the held funds were released. Repeat the cancellation to settle it.", ex);
        }
    }

    // ───────────────────────────── Refund ─────────────────────────────

    public async Task<(OrderPayment Payment, PaymentRefund Refund)> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || !IdempotencyKeyPattern.IsMatch(idempotencyKey))
            throw new PaymentRequestException(PaymentErrorKind.Validation, "idempotency_key_required",
                "An idempotency key (1-64 characters: letters, digits, '.', '_', ':' or '-') is required for refunds.");
        if (amount is not null && amount <= 0)
            throw new PaymentRequestException(PaymentErrorKind.Validation, "invalid_amount", "The refund amount must be greater than zero.");

        await LoadOwnedOrderAsync(orderId, buyerId, ct);
        var payment = await GetPaymentAsync(orderId, ct);
        if (payment is null || payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
            throw new PaymentRequestException(PaymentErrorKind.Conflict, "not_captured",
                $"Order {orderId} has not been fulfilled, so there is no captured payment to refund.");
        if (amount is not null && !MoneyFormat.IsExact(amount.Value, payment.Currency))
            throw new PaymentRequestException(PaymentErrorKind.Validation, "amount_precision",
                $"The refund amount has more precision than {payment.Currency} allows.");

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
            return (payment, await ReplayRefundAsync(payment, existing, amount, ct));

        var keyClaim = $"refund:{payment.Id}:{idempotencyKey}";
        if (!await _claims.TryClaimAsync(keyClaim, ct))
        {
            payment = (await GetPaymentAsync(orderId, ct))!;
            existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
            if (existing is not null)
                return (payment, await ReplayRefundAsync(payment, existing, amount, ct));
            throw InProgress("A refund with this idempotency key is already being processed.");
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0 || refundAmount > payment.RefundableAmount)
        {
            await _claims.ReleaseAsync(keyClaim, CancellationToken.None);
            throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "exceeds_refundable",
                $"Only {MoneyFormat.Format(Math.Max(0, payment.RefundableAmount), payment.Currency)} {payment.Currency} of the captured " +
                $"{MoneyFormat.Format(payment.CapturedAmount ?? 0, payment.Currency)} {payment.Currency} can still be refunded.");
        }

        // One refund at a time per payment: the slot claim makes the balance check above race-free.
        var slot = payment.Refunds.Count + 1;
        var paymentId = payment.Id;
        var refundCount = payment.Refunds.Count;
        if (!await ClaimAsync($"refund-slot:{paymentId}:{slot}", () => refundCount + 1 == slot, ct))
        {
            await _claims.ReleaseAsync(keyClaim, CancellationToken.None);
            throw InProgress("Another refund for this order is being processed; retry shortly.");
        }

        var refund = payment.AddRefund(idempotencyKey, refundAmount, NewRequestId(), Now);
        await _payments.UpdateAsync(payment, ct);
        return (payment, await SendRefundAsync(payment, refund, ct));
    }

    private async Task<PaymentRefund> ReplayRefundAsync(OrderPayment payment, PaymentRefund refund, decimal? amount, CancellationToken ct)
    {
        if (amount is not null && amount != refund.Amount)
            throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "idempotency_key_reused",
                "This idempotency key was already used for a refund of a different amount.");

        switch (refund.Status)
        {
            case RefundStatus.Succeeded:
                return refund;
            case RefundStatus.Failed:
                throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "refund_refused", refund.FailureReason ?? "The refund was refused.");
            default:
                if (refund.OutcomeUnknownSince is null && refund.RequestedAt > Now - _settings.StaleAfter)
                    throw InProgress("This refund is still being processed.");
                return await SendRefundAsync(payment, refund, ct);
        }
    }

    private async Task<PaymentRefund> SendRefundAsync(OrderPayment payment, PaymentRefund refund, CancellationToken ct)
    {
        try
        {
            var result = await WithOneResendOnUnknownAsync(() => _gateway.RefundAsync(payment.CaptureId!, refund.Amount, payment.Currency,
                $"eshop-order-{payment.OrderId}-refund-{refund.Id}", refund.PayPalRequestId, ct));

            if (string.Equals(result.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                payment.RefundFailed(refund, $"PayPal reported the refund as {result.Status}.", Now);
                await _payments.UpdateAsync(payment, CancellationToken.None);
                throw new PaymentRequestException(PaymentErrorKind.Unprocessable, "refund_refused", refund.FailureReason!);
            }

            payment.RefundSucceeded(refund, result.Id, result.Status, Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            return refund;
        }
        catch (PaymentGatewayException ex) when (ex.OutcomeUnknown)
        {
            payment.RefundOutcomeUnknown(refund, Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            throw new PaymentOutcomePendingException(
                "PayPal did not respond in time, so it is not yet known whether the refund went through. Repeat the request with the same idempotency key to settle it; it will not refund twice.", ex);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning("Refund for order {OrderId} refused by PayPal: {Name} {Issue} (debug id {DebugId})",
                payment.OrderId, ex.ProviderErrorName ?? "-", ex.ProviderIssue ?? "-", ex.DebugId ?? "-");
            payment.RefundFailed(refund, DescribeRefusal("PayPal refused the refund", ex), Now);
            await _payments.UpdateAsync(payment, CancellationToken.None);
            throw;
        }
    }

    // ───────────────────────────── Sweeper entry point ─────────────────────────────

    /// <summary>
    /// Settles a payment whose PayPal outcome is unknown or whose in-flight request was lost. Called by the
    /// background sweeper; no card details are available here, so an unfinished authorization is either read
    /// back from PayPal or abandoned (no hold exists without an authorization).
    /// </summary>
    public async Task SettleAsync(int paymentId, CancellationToken ct)
    {
        var payment = await _payments.FirstOrDefaultAsync(new PaymentByIdSpec(paymentId), ct);
        if (payment is null) return;
        var order = await LoadOrderAsync(payment.OrderId, ct);

        try
        {
            switch (payment.Status)
            {
                case PaymentStatus.Authorizing:
                    var authorization = payment.PayPalOrderId is null ? null : await _gateway.GetOrderAsync(payment.PayPalOrderId, ct);
                    if (authorization?.Authorization is not null)
                    {
                        await ApplyAuthorizationAsync(payment, order, authorization, ct);
                    }
                    else
                    {
                        payment.AuthorizationFailed("The payment attempt did not complete; no funds are held. The shopper can pay again.", Now);
                        await _payments.UpdateAsync(payment, ct);
                    }
                    break;
                case PaymentStatus.Reauthorizing or PaymentStatus.Capturing:
                    await ContinueFulfilmentAsync(payment, order, ct);
                    break;
                case PaymentStatus.Voiding:
                    await ContinueVoidAsync(payment, order, ct);
                    break;
            }

            foreach (var refund in payment.Refunds.Where(r => r.Status == RefundStatus.Requested).ToList())
            {
                await SendRefundAsync(payment, refund, ct);
            }
        }
        catch (Exception ex) when (ex is PaymentRequestException or PaymentOutcomePendingException or PaymentGatewayException)
        {
            // Recorded on the payment by the step that failed; the next sweep (or request) tries again.
            _logger.LogWarning("Settling payment {PaymentId} for order {OrderId} did not finish: {Message}", payment.Id, payment.OrderId, ex.Message);
        }
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct) =>
        await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
        ?? throw PaymentRequestException.NotFound($"Order {orderId}");

    /// <summary>Another shopper's order is reported exactly like a missing one.</summary>
    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw PaymentRequestException.NotFound($"Order {orderId}");
        return order;
    }

    private async Task EnsureOrderStatusAsync(Order order, OrderStatus status, CancellationToken ct)
    {
        if (order.Status == status) return;
        if (status == OrderStatus.Fulfilled) order.MarkFulfilled();
        else if (status == OrderStatus.Cancelled) order.MarkCancelled();
        await _orders.UpdateAsync(order, ct);
    }

    private async Task FailSettleActionAsync(OrderPayment payment, string message, CancellationToken ct, string? authorizationStatus = null)
    {
        payment.SettleActionFailed(message, authorizationStatus, Now);
        await _payments.UpdateAsync(payment, CancellationToken.None);
        await _claims.ReleaseAsync(SettleClaimKey(payment), CancellationToken.None);
    }

    private async Task<GatewayAuthorization?> TryGetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try { return await _gateway.GetAuthorizationAsync(authorizationId, ct); }
        catch (PaymentGatewayException) { return null; }
    }

    /// <summary>
    /// Takes a claim. If the store refuses it, the claim is only reclaimed when it is older than any live request
    /// could be AND <paramref name="noProgressRecorded"/> shows its owner never recorded anything (it crashed).
    /// </summary>
    private async Task<bool> ClaimAsync(string key, Func<bool> noProgressRecorded, CancellationToken ct)
    {
        if (await _claims.TryClaimAsync(key, ct)) return true;
        if (!noProgressRecorded()) return false;
        var claimedAt = await _claims.GetClaimedAtAsync(key, ct);
        if (claimedAt is null || claimedAt > Now - _settings.StaleAfter) return false;
        await _claims.ReleaseAsync(key, ct);
        return await _claims.TryClaimAsync(key, ct);
    }

    /// <summary>
    /// Settles an unknown write in its own catch by re-sending once under the same PayPal-Request-Id
    /// (the caller's lambda carries the stored id), which PayPal answers with the original result.
    /// Skipped when the request's PayPal time budget is already spent.
    /// </summary>
    private static async Task<T> WithOneResendOnUnknownAsync<T>(Func<Task<T>> send)
    {
        try
        {
            return await send();
        }
        catch (PaymentGatewayException ex) when (ex.OutcomeUnknown && !ex.BudgetExhausted)
        {
            return await send();
        }
    }

    private bool NeedsSettlement(OrderPayment payment) =>
        payment.OutcomeUnknownSince is not null || payment.UpdatedAt <= Now - _settings.StaleAfter;

    private static string SettleClaimKey(OrderPayment payment) => $"settle:{payment.Id}";
    private static string CustomId(int orderId) => $"eshop-order-{orderId}";
    private static string NewInvoiceId(int orderId) => $"eshop-{orderId}-{Guid.NewGuid():N}";
    private static string NewRequestId() => Guid.NewGuid().ToString("D");

    private static PaymentRequestException InProgress(string message) => new(PaymentErrorKind.InProgress, "in_progress", message);

    internal static string DescribeRefusal(string prefix, PaymentGatewayException ex)
    {
        var detail = ex.ProviderIssue is not null ? $"{ex.ProviderIssue}: {ex.Message}" : ex.Message;
        return ex.DebugId is not null ? $"{prefix} — {detail} (PayPal debug id {ex.DebugId})." : $"{prefix} — {detail}.";
    }
}
