using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund against a captured payment. Part of the <see cref="Payment"/> aggregate.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key: repeating a refund request under the
/// same key must return this same refund rather than issuing another.
/// </summary>
public class Refund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(string idempotencyKey, decimal amount, string? payPalRefundId, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        PayPalRefundId = payPalRefundId;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
