using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against an order's captured payment. Belongs to the
/// <see cref="OrderPayment"/> aggregate. The <see cref="IdempotencyKey"/> is the caller-supplied
/// key that makes a repeated refund request a no-op while allowing genuinely distinct partial
/// refunds of the same capture.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        State = RefundState.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owning payment id (FK, set by EF).</summary>
    public int OrderPaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key, unique within the owning payment.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's id for the refund; also returned to the caller as <c>refundId</c>.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>Status PayPal reported for the refund (e.g. COMPLETED, PENDING).</summary>
    public string? PayPalStatus { get; private set; }

    public RefundState State { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Succeed(string payPalRefundId, string payPalStatus)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        PayPalRefundId = payPalRefundId;
        PayPalStatus = payPalStatus;
        State = RefundState.Completed;
    }

    public void Fail()
    {
        State = RefundState.Failed;
    }

    /// <summary>
    /// Whether this refund still reserves part of the captured amount. A failed refund releases
    /// its reservation; a pending or completed one holds it, so the total can never exceed capture.
    /// </summary>
    public bool CountsTowardTotal => State != RefundState.Failed;
}
