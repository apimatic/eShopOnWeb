using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against an order's captured payment. Part of the
/// <see cref="OrderPayment"/> aggregate (not an aggregate root on its own).
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(int orderPaymentId, string idempotencyKey, decimal amount,
        string? payPalRefundId, string status, string? noteToPayer)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderPaymentId = orderPaymentId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        PayPalRefundId = payPalRefundId;
        Status = status;
        NoteToPayer = noteToPayer;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key; unique per payment.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's own refund id.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal's current refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public string? NoteToPayer { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Whether this refund committed money (and therefore consumes the refundable balance).</summary>
    public bool CountsTowardRefunded => Status is "COMPLETED" or "PENDING";
}
