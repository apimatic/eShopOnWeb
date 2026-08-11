using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a <see cref="Payment"/>'s capture. Multiple partial refunds are allowed
/// up to the captured amount. The <see cref="IdempotencyKey"/> is caller-supplied: repeating a request under
/// the same key returns the same refund rather than issuing a second one.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key. Unique per payment.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    /// <summary>PayPal's refund id.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal's refund status (e.g. COMPLETED, PENDING, FAILED).</summary>
    public string? Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void SetResult(string payPalRefundId, string status)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
    }

    /// <summary>True when PayPal reports the refund as completed (money returned).</summary>
    public bool IsSuccessful =>
        string.Equals(Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, "PENDING", StringComparison.OrdinalIgnoreCase);
}
