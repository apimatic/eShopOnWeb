using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the order's captured payment. Part of the Order aggregate
/// (owned by <see cref="Payment"/>). The <see cref="IdempotencyKey"/> is the caller-supplied key
/// that makes repeating a refund request a no-op while allowing distinct partial refunds.
/// </summary>
public class PaymentRefund
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string refundId, string idempotencyKey, decimal amount, string currency, string status)
    {
        RefundId = refundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's refund id.</summary>
    public string RefundId { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>PayPal refund status, e.g. COMPLETED / PENDING.</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
