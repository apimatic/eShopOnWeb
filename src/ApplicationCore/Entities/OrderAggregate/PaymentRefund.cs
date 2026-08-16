using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a captured payment. Two distinct partial refunds of the same
/// capture are legitimate; a repeat under the same <see cref="IdempotencyKey"/> is not and must be
/// collapsed to the original refund.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string payPalRefundId, decimal amount, string currency, string status)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Currency = currency;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }

    /// <summary>Caller-supplied key that makes the refund request idempotent.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's own identifier for the refund.</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
