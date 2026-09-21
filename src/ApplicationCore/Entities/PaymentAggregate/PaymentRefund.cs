using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against an <see cref="OrderPayment"/>'s capture. Part of the
/// OrderPayment aggregate (not an aggregate root of its own).
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        PayPalRefundId = Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Amount = amount;
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal-generated refund id (the id a later lookup acts on).</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>Amount refunded by this refund.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
