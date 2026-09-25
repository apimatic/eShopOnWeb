using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// One refund issued against a <see cref="Payment"/>'s capture. Part of the Payment aggregate
/// (an owned entity). <see cref="IdempotencyKey"/> is the caller-supplied key: repeating a refund
/// request under the same key must not refund twice, while two distinct keys are two legitimate
/// partial refunds of the same capture.
/// </summary>
public class PaymentRefund
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal-generated refund id.</summary>
    public string RefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
