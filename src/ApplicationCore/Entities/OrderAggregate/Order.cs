using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() {}

    public Order(string buyerId, Address? shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;

    /// <summary>Null for orders placed through the API without a shipping address.</summary>
    public Address? ShipToAddress { get; private set; }

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();

    // Using List<>.AsReadOnly()
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    // Payment state. Every change below rotates PaymentVersion, which is persisted as a concurrency token:
    // two requests that both loaded the same version cannot both save, so a payment or refund decision is
    // always taken against the latest state.
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public Guid PaymentVersion { get; private set; } = Guid.NewGuid();

    private readonly List<OrderPaymentAttempt> _paymentAttempts = new List<OrderPaymentAttempt>();
    public IReadOnlyCollection<OrderPaymentAttempt> PaymentAttempts => _paymentAttempts.AsReadOnly();

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    private readonly List<PaymentProviderRecordEntry> _paymentRecord = new List<PaymentProviderRecordEntry>();
    public IReadOnlyCollection<PaymentProviderRecordEntry> PaymentRecord => _paymentRecord.AsReadOnly();

    public OrderPaymentAttempt? AuthorisedPayment =>
        _paymentAttempts.FirstOrDefault(a => a.Status == PaymentAttemptStatus.Authorised);

    /// <summary>Minor units given back (or on their way back) — every refund the provider has not rejected.</summary>
    public long RefundedMinor => _refunds.Where(r => r.CountsAgainstBalance).Sum(r => r.AmountMinor);

    /// <summary>Minor units that may still be refunded.</summary>
    public long RefundableMinor => AuthorisedPayment is { } payment ? Math.Max(0, payment.AmountMinor - RefundedMinor) : 0;

    /// <summary>
    /// Decides whether a payment may be sent now. Starts a new attempt, re-opens one whose outcome is unknown
    /// (to be re-sent with its original idempotency key), or says why nothing may be sent.
    /// </summary>
    public PaymentClaim ClaimPayment(long amountMinor, string currency, DateTimeOffset now, TimeSpan staleAfter)
    {
        if (AuthorisedPayment is not null)
            return new PaymentClaim(PaymentClaimKind.AlreadyPaid, AuthorisedPayment);

        var open = _paymentAttempts.Where(a => a.IsOpen).OrderByDescending(a => a.AttemptNumber).FirstOrDefault();
        if (open is not null)
        {
            if (open.Status == PaymentAttemptStatus.Pending)
                return new PaymentClaim(PaymentClaimKind.AwaitingProviderOutcome, open);

            var stale = now - open.LastSentAt >= staleAfter;
            if (open.Status == PaymentAttemptStatus.InFlight && !stale)
                return new PaymentClaim(PaymentClaimKind.InProgress, open);

            open.MarkResent(now);
            RecomputePaymentStatus();
            return new PaymentClaim(PaymentClaimKind.Resend, open);
        }

        var attemptNumber = _paymentAttempts.Count == 0 ? 1 : _paymentAttempts.Max(a => a.AttemptNumber) + 1;
        var attempt = new OrderPaymentAttempt(Id, attemptNumber, amountMinor, currency, now);
        _paymentAttempts.Add(attempt);
        RecomputePaymentStatus();
        return new PaymentClaim(PaymentClaimKind.Send, attempt);
    }

    public void RecordPaymentOutcome(int attemptNumber, PaymentAttemptStatus status, string? pspReference,
        string? resultCode, string? refusalReason, string? refusalReasonCode, string? shopperMessage, DateTimeOffset now)
    {
        var attempt = _paymentAttempts.SingleOrDefault(a => a.AttemptNumber == attemptNumber)
            ?? throw new InvalidOperationException($"Order {Id} has no payment attempt {attemptNumber}.");
        attempt.RecordOutcome(status, pspReference, resultCode, refusalReason, refusalReasonCode, shopperMessage, now);
        RecomputePaymentStatus();
    }

    /// <summary>
    /// Decides whether a refund may be sent now. An existing refund with the same id is returned (or re-opened
    /// when its outcome is unknown) instead of creating a second one.
    /// </summary>
    public RefundClaim ClaimRefund(Guid refundId, string idempotencyKey, long? amountMinor, string? reason,
        string requestedBy, DateTimeOffset now, TimeSpan staleAfter)
    {
        var existing = _refunds.SingleOrDefault(r => r.Id == refundId);
        if (existing is not null)
        {
            if (TryReopenRefund(existing, now, staleAfter))
                return new RefundClaim(RefundClaimKind.Resend, existing);
            return new RefundClaim(existing.Status == RefundStatus.Requested ? RefundClaimKind.InProgress : RefundClaimKind.Existing, existing);
        }

        var payment = AuthorisedPayment
            ?? throw new OrderPaymentException(OrderPaymentError.NotPaid, $"Order {Id} has no captured payment to refund.");

        var refundable = RefundableMinor;
        if (refundable <= 0)
            throw new OrderPaymentException(OrderPaymentError.NothingToRefund, $"Order {Id} has already been refunded in full.");

        var amount = amountMinor ?? refundable;
        if (amount <= 0)
            throw new OrderPaymentException(OrderPaymentError.InvalidAmount, "The refund amount must be greater than zero.");
        if (amount > refundable)
            throw new OrderPaymentException(OrderPaymentError.ExceedsRefundable,
                $"The refund of {Money.Format(amount, payment.Currency)} exceeds the " +
                $"{Money.Format(refundable, payment.Currency)} still refundable on order {Id}.");

        var refund = new OrderRefund(refundId, Id, payment.AttemptNumber, idempotencyKey, amount, payment.Currency,
            reason, requestedBy, now);
        _refunds.Add(refund);
        RecomputePaymentStatus();
        return new RefundClaim(RefundClaimKind.Send, refund);
    }

    /// <summary>Re-opens a refund whose outcome is unknown (or whose sender vanished) so it can be re-sent.</summary>
    public bool TryReopenRefund(Guid refundId, DateTimeOffset now, TimeSpan staleAfter)
    {
        var refund = _refunds.SingleOrDefault(r => r.Id == refundId);
        return refund is not null && TryReopenRefund(refund, now, staleAfter);
    }

    private bool TryReopenRefund(OrderRefund refund, DateTimeOffset now, TimeSpan staleAfter)
    {
        var unsettled = refund.Status == RefundStatus.Unknown
            || (refund.Status == RefundStatus.Requested && now - refund.LastSentAt >= staleAfter);
        if (!unsettled)
            return false;

        refund.MarkResent(now);
        RotatePaymentVersion();
        return true;
    }

    public void RecordRefundOutcome(Guid refundId, RefundStatus status, string? pspReference, string? failureMessage,
        DateTimeOffset now)
    {
        var refund = _refunds.SingleOrDefault(r => r.Id == refundId)
            ?? throw new InvalidOperationException($"Order {Id} has no refund {refundId}.");
        refund.RecordOutcome(status, pspReference, failureMessage, now);
        RecomputePaymentStatus();
    }

    public void AddPaymentRecordEntry(string provider, string operation, int? paymentAttemptNumber, Guid? refundId,
        string idempotencyKey, string merchantReference, int? httpStatus, string? responseBody, string? transportError,
        DateTimeOffset recordedAt)
    {
        _paymentRecord.Add(new PaymentProviderRecordEntry(Id, provider, operation, paymentAttemptNumber, refundId,
            idempotencyKey, merchantReference, httpStatus, responseBody, transportError, recordedAt));
        RotatePaymentVersion();
    }

    public void RecomputePaymentStatus()
    {
        var payment = AuthorisedPayment;
        if (payment is not null)
        {
            var refunded = RefundedMinor;
            PaymentStatus = refunded <= 0
                ? OrderPaymentStatus.Paid
                : refunded >= payment.AmountMinor ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
        }
        else
        {
            PaymentStatus = _paymentAttempts.Any(a => a.IsOpen)
                ? OrderPaymentStatus.PaymentPending
                : OrderPaymentStatus.AwaitingPayment;
        }
        RotatePaymentVersion();
    }

    private void RotatePaymentVersion() => PaymentVersion = Guid.NewGuid();
}

public enum PaymentClaimKind
{
    /// <summary>A new attempt was claimed; send it.</summary>
    Send,

    /// <summary>An attempt whose outcome is unknown was re-claimed; re-send it with its original idempotency key.</summary>
    Resend,

    /// <summary>The order is already paid; nothing to send.</summary>
    AlreadyPaid,

    /// <summary>Another request is sending a payment for this order right now.</summary>
    InProgress,

    /// <summary>The provider accepted a payment but has not given its final outcome.</summary>
    AwaitingProviderOutcome
}

public sealed record PaymentClaim(PaymentClaimKind Kind, OrderPaymentAttempt Attempt);

public enum RefundClaimKind
{
    Send,
    Resend,

    /// <summary>A refund with this id already exists and is settled.</summary>
    Existing,

    /// <summary>A refund with this id is being sent by another request.</summary>
    InProgress
}

public sealed record RefundClaim(RefundClaimKind Kind, OrderRefund Refund);
