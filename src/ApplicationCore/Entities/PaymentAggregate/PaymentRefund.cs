using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against the captured payment. Child entity of the
/// <see cref="OrderPayment"/> aggregate.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, decimal amount, string paypalRefundId, string status)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        PayPalRefundId = paypalRefundId;
        Status = status;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied idempotency key; a repeat under the same key returns this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's refund id.</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>PayPal's refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
