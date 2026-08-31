using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single (full or partial) refund issued against a captured payment.
/// The caller-supplied idempotency key guarantees a repeated request never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() {}

    public PaymentRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status, string? noteToPayer)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
        NoteToPayer = noteToPayer;
    }

    public string PayPalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public string? NoteToPayer { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
