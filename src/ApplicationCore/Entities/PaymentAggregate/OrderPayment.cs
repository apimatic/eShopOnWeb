using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment and fulfilment state for a single eShop <see cref="OrderAggregate.Order"/>.
/// One <see cref="OrderPayment"/> exists per order; it carries enough of the state PayPal owns
/// (the ids and current status of the hold, the capture and the refunds) that a later request
/// can act on it, not only the one that started it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
    #pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currency, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        Amount = amount;
        Status = PaymentStatus.PendingPayment;
        // A per-order token that is stable across retries of the same operation (so a double-click
        // deduplicates) yet unique across process restarts, since the in-memory order id restarts at 1
        // while PayPal retains request ids for hours.
        IdempotencyToken = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }

    /// <summary>Stable, globally-unique seed for the PayPal-Request-Id of this order's payment operations.</summary>
    public string IdempotencyToken { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }

    /// <summary>The order total, in the currency's major units. This is the amount held/captured.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // ---- PayPal-owned state ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGross { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }

    /// <summary>A short, operator-actionable note about the last failure, when <see cref="Status"/> is Failed.</summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsAuthorized => Status == PaymentStatus.Authorized;
    public bool IsFulfilled => Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded;

    /// <summary>Amount still available to refund (captured gross minus what has already been refunded).</summary>
    public decimal RefundableRemaining => (CapturedGross ?? 0m) - RefundedAmount;

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Authorized;
        FailureReason = null;
        Touch();
    }

    public void RecordCapture(string captureId, string effectiveAuthorizationId, string captureStatus,
        decimal gross, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(effectiveAuthorizationId, nameof(effectiveAuthorizationId));

        AuthorizationId = effectiveAuthorizationId;
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = gross;
        PayPalFee = fee;
        NetAmount = net;
        Status = PaymentStatus.Fulfilled;
        FailureReason = null;
        Touch();
    }

    public void RecordCancellation()
    {
        Status = PaymentStatus.Cancelled;
        AuthorizationStatus = "VOIDED";
        Touch();
    }

    /// <summary>
    /// Records a refund against the capture. Enforces that total refunds never exceed the captured
    /// amount, so a partly-refunded order never becomes refundable beyond what was captured.
    /// </summary>
    public PaymentRefund RecordRefund(string refundId, decimal amount, string idempotencyKey, string refundStatus)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var refund = new PaymentRefund(refundId, amount, idempotencyKey, refundStatus);
        _refunds.Add(refund);
        RefundedAmount += amount;
        Status = RefundedAmount >= (CapturedGross ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void MarkFailed(string reason)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
