using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Takes payment for orders and refunds them. Every provider write follows the same order:
/// claim a row the store can hold only once → call the provider → record what it returned.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IOrderPaymentStore _store;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<OrderPaymentService> _logger;
    private readonly TimeProvider _clock;

    public OrderPaymentService(IOrderPaymentStore store, IPaymentGateway gateway,
        IAppLogger<OrderPaymentService> logger, TimeProvider clock)
    {
        _store = store;
        _gateway = gateway;
        _logger = logger;
        _clock = clock;
    }

    public async Task<PayOrderResult> PayAsync(int orderId, string buyerId, EncryptedCardDetails card,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var order = await _store.GetOrderWithPaymentsAsync(orderId, cancellationToken);
        // Someone else's order is reported exactly like a missing one, so ids cannot be probed.
        if (order is null || order.BuyerId != buyerId)
        {
            return new PayOrderResult(PayOrderOutcome.OrderNotFound, orderId, OrderPaymentStatus.AwaitingPayment, null,
                "Order not found.");
        }

        var authorised = order.AuthorisedPayment;
        if (authorised != null)
        {
            return Result(PayOrderOutcome.AlreadyPaid, order, authorised, "This order has already been paid. You were charged once.");
        }

        // A previous attempt whose outcome is unknown is settled first, with its own idempotency key, so a
        // payment the provider did take is found instead of charged a second time.
        var unknown = order.PaymentAttempts.FirstOrDefault(a => a.Status == PaymentAttemptStatus.Unknown);
        if (unknown != null)
        {
            _logger.LogInformation("Settling payment attempt {AttemptNumber} of order {OrderId} whose outcome is unknown",
                unknown.AttemptNumber, orderId);
            return await SendAndRecordAsync(order, unknown, card);
        }

        var blocking = order.PaymentAttempts.FirstOrDefault(a => a.BlocksNewAttempts);
        if (blocking != null)
        {
            return Result(PayOrderOutcome.InProgress, order, blocking,
                "A payment for this order is already being processed. Please don't pay again; check the order status shortly.");
        }

        var currency = _gateway.Currency;
        var total = order.Total();
        if (!MinorUnits.TryFromDecimal(total, currency, out var amountMinorUnits) || amountMinorUnits <= 0)
        {
            _logger.LogWarning("Order {OrderId} total {Total} cannot be charged exactly in {Currency}", orderId, total, currency);
            return Result(PayOrderOutcome.AmountNotChargeable, order, null,
                $"The order total {total} cannot be charged in {currency}.");
        }

        var nextAttemptNumber = order.PaymentAttempts.Select(a => a.AttemptNumber).DefaultIfEmpty(0).Max() + 1;
        var attempt = new OrderPaymentAttempt(orderId, nextAttemptNumber, amountMinorUnits, currency, _clock.GetUtcNow());

        // Claim: the store refuses a second row with this (OrderId, AttemptNumber) — a concurrent double-click
        // that read the same attempt history stops here, before it reaches the provider.
        if (!await _store.TryClaimPaymentAttemptAsync(attempt, cancellationToken))
        {
            _logger.LogWarning("Payment attempt {AttemptNumber} of order {OrderId} was already claimed by a concurrent request",
                nextAttemptNumber, orderId);
            return new PayOrderResult(PayOrderOutcome.InProgress, orderId, OrderPaymentStatus.PaymentPending, null,
                "A payment for this order is already being processed. Please don't pay again; check the order status shortly.");
        }

        // With our slot held, any earlier attempt that may still charge (claimed by a request that read the
        // history before we did) wins, and ours is withdrawn without being sent.
        var earlier = (await _store.ReadPaymentAttemptsAsync(orderId, cancellationToken))
            .Where(a => a.AttemptNumber < attempt.AttemptNumber && a.BlocksNewAttempts)
            .OrderBy(a => a.AttemptNumber)
            .FirstOrDefault();
        if (earlier != null)
        {
            attempt.Withdraw(_clock.GetUtcNow());
            await _store.SaveChangesAsync(CancellationToken.None);
            return earlier.Status == PaymentAttemptStatus.Authorised
                ? new PayOrderResult(PayOrderOutcome.AlreadyPaid, orderId, OrderPaymentStatus.Paid, earlier,
                    "This order has already been paid. You were charged once.")
                : new PayOrderResult(PayOrderOutcome.InProgress, orderId, OrderPaymentStatus.PaymentPending, earlier,
                    "A payment for this order is already being processed. Please don't pay again; check the order status shortly.");
        }

        return await SendAndRecordAsync(order, attempt, card);
    }

    private async Task<PayOrderResult> SendAndRecordAsync(Order order, OrderPaymentAttempt attempt, EncryptedCardDetails card)
    {
        // The provider call and the write that records it are deliberately not tied to the caller's
        // connection: a shopper closing the tab must not leave a charge unrecorded.
        var result = await _gateway.AuthoriseCardPaymentAsync(
            new CardPaymentRequest(attempt.MerchantReference, attempt.IdempotencyKey, attempt.AmountMinorUnits,
                attempt.Currency, card),
            CancellationToken.None);

        var status = result.Outcome switch
        {
            PaymentProviderOutcome.Authorised => PaymentAttemptStatus.Authorised,
            PaymentProviderOutcome.Refused => PaymentAttemptStatus.Refused,
            PaymentProviderOutcome.ActionRequired => PaymentAttemptStatus.ActionRequired,
            PaymentProviderOutcome.Pending => PaymentAttemptStatus.Pending,
            PaymentProviderOutcome.Unknown => PaymentAttemptStatus.Unknown,
            _ => PaymentAttemptStatus.Rejected
        };
        attempt.Record(status, result, _clock.GetUtcNow());
        await _store.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation(
            "Payment attempt {AttemptNumber} of order {OrderId}: outcome {Outcome}, resultCode {ResultCode}, pspReference {PspReference}",
            attempt.AttemptNumber, order.Id, result.Outcome, result.ResultCode ?? "-", result.PspReference ?? "-");

        return result.Outcome switch
        {
            PaymentProviderOutcome.Authorised => Result(PayOrderOutcome.Paid, order, attempt, "Payment successful."),
            PaymentProviderOutcome.Refused => Result(PayOrderOutcome.Refused, order, attempt, RefusalMessage(result)),
            PaymentProviderOutcome.Rejected => Result(PayOrderOutcome.CardDetailsRejected, order, attempt,
                "Your card details could not be processed" + Detail(result.ErrorMessage) +
                ". Please re-enter them, or use a different card. You have not been charged."),
            PaymentProviderOutcome.ActionRequired => Result(PayOrderOutcome.AuthenticationNotSupported, order, attempt,
                "Your bank requires an extra verification step (such as 3-D Secure) that this checkout cannot complete. " +
                "Please use a different card. You have not been charged."),
            PaymentProviderOutcome.ProviderUnavailable => Result(PayOrderOutcome.ProviderUnavailable, order, attempt,
                "Payments are temporarily unavailable. You have not been charged; please try again later."),
            _ => Result(PayOrderOutcome.Pending, order, attempt,
                "We are still confirming your payment with the bank. Please don't pay again; check the order status shortly.")
        };
    }

    private static string RefusalMessage(PaymentProviderResult result) =>
        "Your card was declined" + Detail(result.RefusalReason) +
        ". Please check the card number, expiry date and security code, or use a different card. You have not been charged.";

    private static string Detail(string? reason) => string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason})";

    private static PayOrderResult Result(PayOrderOutcome outcome, Order order, OrderPaymentAttempt? attempt, string message) =>
        new(outcome, order.Id, order.PaymentStatus, attempt, message);

    public async Task<RefundOrderResult> RefundAsync(int orderId, decimal? amount, string? reason, string requestedBy,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(requestedBy, nameof(requestedBy));

        var order = await _store.GetOrderWithPaymentsAsync(orderId, cancellationToken);
        if (order is null)
        {
            return new RefundOrderResult(RefundOrderOutcome.OrderNotFound, orderId, null, 0, null, "Order not found.");
        }

        var payment = order.AuthorisedPayment;
        if (payment?.PspReference is null)
        {
            return new RefundOrderResult(RefundOrderOutcome.NotPaid, orderId, null, 0, null,
                "This order has no successful payment to refund.");
        }

        // Refunds whose outcome is unknown are settled first (same idempotency key, same body), so the
        // refundable amount below reflects what the provider actually did.
        foreach (var unknown in order.Refunds.Where(r => r.Status == RefundStatus.Unknown).OrderBy(r => r.Sequence).ToList())
        {
            _logger.LogInformation("Settling refund {RefundId} whose outcome is unknown", unknown.RefundId);
            await SendAndRecordAsync(unknown);
        }

        var reserved = order.Refunds.Where(r => r.ReservesAmount).Sum(r => r.AmountMinorUnits);
        var refundable = payment.AmountMinorUnits - reserved;

        long amountMinorUnits;
        if (amount is null)
        {
            amountMinorUnits = refundable;
        }
        else if (!MinorUnits.TryFromDecimal(amount.Value, payment.Currency, out amountMinorUnits) || amountMinorUnits <= 0)
        {
            return Refund(RefundOrderOutcome.InvalidAmount, orderId, null, refundable, payment.Currency,
                $"The refund amount must be a positive amount in {payment.Currency} with at most {MinorUnits.Exponent(payment.Currency)} decimals.");
        }

        if (amountMinorUnits <= 0 || amountMinorUnits > refundable)
        {
            return Refund(RefundOrderOutcome.ExceedsRefundable, orderId, null, refundable, payment.Currency,
                $"At most {MinorUnits.ToDecimal(refundable, payment.Currency)} {payment.Currency} can still be refunded on this order.");
        }

        var nextSequence = order.Refunds.Select(r => r.Sequence).DefaultIfEmpty(0).Max() + 1;
        var refund = new OrderRefund(orderId, nextSequence, payment.PspReference, amountMinorUnits, payment.Currency,
            reason, requestedBy, _clock.GetUtcNow());

        // Claim: the store refuses a second refund with this (OrderId, Sequence), so two operators refunding
        // at once cannot both pass the refundable check on the same history.
        if (!await _store.TryClaimRefundAsync(refund, cancellationToken))
        {
            return Refund(RefundOrderOutcome.InProgress, orderId, null, refundable, payment.Currency,
                "Another refund of this order is being processed. Reload the order and try again.");
        }

        // With our slot held, every earlier refund is visible and its amount reserved; re-check against them.
        var reservedBefore = (await _store.ReadRefundsAsync(orderId, cancellationToken))
            .Where(r => r.Sequence < refund.Sequence && r.ReservesAmount)
            .Sum(r => r.AmountMinorUnits);
        var refundableNow = payment.AmountMinorUnits - reservedBefore;
        if (amountMinorUnits > refundableNow)
        {
            refund.Reject("Exceeds the refundable amount.", _clock.GetUtcNow());
            await _store.SaveChangesAsync(CancellationToken.None);
            return Refund(RefundOrderOutcome.ExceedsRefundable, orderId, refund, refundableNow, payment.Currency,
                $"At most {MinorUnits.ToDecimal(refundableNow, payment.Currency)} {payment.Currency} can still be refunded on this order.");
        }

        var result = await SendAndRecordAsync(refund);
        var remaining = payment.AmountMinorUnits - reservedBefore - (refund.ReservesAmount ? refund.AmountMinorUnits : 0);

        return result.Outcome switch
        {
            RefundProviderOutcome.Received => Refund(RefundOrderOutcome.Received, orderId, refund, remaining, payment.Currency,
                "Refund accepted by the payment provider."),
            RefundProviderOutcome.Unknown => Refund(RefundOrderOutcome.Pending, orderId, refund, remaining, payment.Currency,
                "The refund was sent but its outcome could not be confirmed yet. The amount stays reserved; do not refund it again."),
            RefundProviderOutcome.ProviderUnavailable => Refund(RefundOrderOutcome.ProviderUnavailable, orderId, refund, remaining,
                payment.Currency, "The payment provider is unavailable. Nothing was refunded; try again later."),
            _ => Refund(RefundOrderOutcome.Rejected, orderId, refund, remaining, payment.Currency,
                "The payment provider rejected the refund" + Detail(result.ErrorMessage) + ".")
        };
    }

    private async Task<RefundProviderResult> SendAndRecordAsync(OrderRefund refund)
    {
        var result = await _gateway.RefundAsync(
            new ProviderRefundRequest(refund.PaymentPspReference, refund.MerchantReference, refund.IdempotencyKey,
                refund.AmountMinorUnits, refund.Currency),
            CancellationToken.None);

        var status = result.Outcome switch
        {
            RefundProviderOutcome.Received => RefundStatus.Received,
            RefundProviderOutcome.Unknown => RefundStatus.Unknown,
            _ => RefundStatus.Failed
        };
        refund.Record(status, result, _clock.GetUtcNow());
        await _store.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation("Refund {RefundId}: outcome {Outcome}, pspReference {PspReference}",
            refund.RefundId, result.Outcome, result.PspReference ?? "-");
        return result;
    }

    private static RefundOrderResult Refund(RefundOrderOutcome outcome, int orderId, OrderRefund? refund,
        long refundable, string? currency, string message) =>
        new(outcome, orderId, refund, refundable, currency, message);
}
