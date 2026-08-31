using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single (full or partial) refund against a captured payment. The caller-supplied
/// idempotency key guarantees a repeated request never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string payPalRefundId, decimal amount, string currency, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Currency = currency;
        Status = status;
    }

    public int PaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
