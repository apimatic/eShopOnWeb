using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured payment. A capture may carry several
/// (distinct partial refunds are legitimate); each is tied to the caller-supplied
/// idempotency key that created it, so a repeat under the same key never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public string PayPalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
    }
}
