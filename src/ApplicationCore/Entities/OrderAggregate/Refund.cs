using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund against the captured <see cref="Payment"/>. Refunds are carried inside the
/// Order aggregate so the running refunded total can be checked against the captured amount.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key that makes repeated refund
/// requests safe: replaying a request under the same key must not refund twice.
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

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's own id for the refund (its record of the money returned).</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>Current PayPal refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string status) => Status = status;
}
