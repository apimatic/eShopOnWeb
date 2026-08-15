using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a captured <see cref="Payment"/>. Part of the Order aggregate.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key that makes a repeated refund
/// request a no-op rather than a second refund.
/// </summary>
public class Refund : BaseEntity
{
    public string RefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(string refundId, string status, decimal amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        RefundId = refundId;
        Status = status;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
    }
}
