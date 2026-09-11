using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured <see cref="Payment"/>. Part of the Payment
/// aggregate; created only through <see cref="Payment.AddRefund"/>.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    internal PaymentRefund(string idempotencyKey, decimal amount)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = "PENDING";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied key that makes a refund request idempotent.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>Amount refunded, in the order currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's identifier for this refund, once issued.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal's current status for this refund (COMPLETED, PENDING, ...).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal void MarkIssued(string payPalRefundId, string status)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
    }
}
