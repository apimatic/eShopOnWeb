using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the capture of an <see cref="OrderPayment"/>. Persisted as
/// part of the Order aggregate (an owned collection of the payment) so that a later request can
/// see the refunds PayPal already knows about and never refund beyond what was captured.
/// </summary>
public class PaymentRefund // Owned entity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The PayPal-generated refund id.</summary>
    public string RefundId { get; private set; }

    /// <summary>The refunded amount, in the payment currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The status PayPal reported for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
