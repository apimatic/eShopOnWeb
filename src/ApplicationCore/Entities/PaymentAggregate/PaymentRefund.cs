using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund made against a captured payment. The caller-supplied
/// <see cref="IdempotencyKey"/> is what makes repeating a refund request safe:
/// a repeat under the same key returns this record instead of refunding again.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string refundId, decimal amount, string currency, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        RefundId = refundId;
        Amount = amount;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's own id for this refund (returned as <c>refundId</c>).</summary>
    public string RefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    /// <summary>The caller-supplied idempotency key this refund was created under.</summary>
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }
}
