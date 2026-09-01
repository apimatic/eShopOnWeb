using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single (full or partial) refund issued against an order's captured payment.
/// The caller-supplied idempotency key guarantees a repeated request never refunds twice.
/// </summary>
public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(string payPalRefundId, decimal amount, string currency, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Currency = currency;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }

    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    /// <summary>PayPal's refund status wire value (COMPLETED, PENDING, ...).</summary>
    public string Status { get; private set; }

    /// <summary>Caller-supplied key; unique per logical refund request.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Statuses that count against the refundable remainder.</summary>
    public bool ConsumesRefundableAmount => Status is "COMPLETED" or "PENDING";
}
