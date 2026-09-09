using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a captured payment. Part of the Order aggregate (via Payment).
/// Records PayPal's own refund id and the caller-supplied idempotency key that produced it, so a
/// repeated request under the same key can be short-circuited instead of refunding twice.
/// </summary>
public class Refund : BaseEntity
{
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key. Unique per distinct refund of a capture.</summary>
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(string payPalRefundId, decimal amount, string currency, string status, string idempotencyKey)
    {
        PayPalRefundId = Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Amount = Guard.Against.Negative(amount, nameof(amount));
        Currency = Guard.Against.NullOrEmpty(currency, nameof(currency));
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
    }
}
