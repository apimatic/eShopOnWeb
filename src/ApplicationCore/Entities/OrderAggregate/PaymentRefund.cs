using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a captured <see cref="Payment"/>. A capture may have several
/// distinct partial refunds. The caller-supplied <see cref="IdempotencyKey"/> is what guards
/// against a repeated request refunding twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string refundId, decimal amount, string currency, string status, string idempotencyKey)
    {
        RefundId = refundId;
        Amount = amount;
        Currency = currency;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The PayPal-generated refund id.</summary>
    public string RefundId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>PayPal refund status (COMPLETED, PENDING, CANCELLED, FAILED).</summary>
    public string Status { get; private set; }

    /// <summary>The idempotency key the caller supplied for this refund request.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A refund that did not (or will not) return money, so it must not reduce the refundable balance.</summary>
    public bool ReducesRefundableBalance =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
