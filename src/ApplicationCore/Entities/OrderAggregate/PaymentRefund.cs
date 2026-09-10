using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a captured payment. A capture may have many
/// (partial) refunds; each carries its own PayPal refund id and the caller-supplied
/// idempotency key that produced it.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public System.DateTimeOffset CreatedAt { get; private set; } = System.DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, decimal amount, string? payPalRefundId, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        PayPalRefundId = payPalRefundId;
        Status = status;
    }
}
