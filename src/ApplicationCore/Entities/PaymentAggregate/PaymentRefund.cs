using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against the captured payment. PayPal owns the authoritative record; we keep
/// its id and status so the refund can be looked up later, plus the caller-supplied idempotency key so a
/// repeated refund request under the same key is never applied twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey, string? reason)
    {
        PayPalRefundId = Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Amount = Guard.Against.NegativeOrZero(amount, nameof(amount));
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Reason = reason;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
