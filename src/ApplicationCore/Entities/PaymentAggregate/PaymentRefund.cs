using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
    public const string RefundStatusCompleted = "COMPLETED";
    public const string RefundStatusPending = "PENDING";

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() {}

    public PaymentRefund(int paymentId, string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        PaymentId = paymentId;
        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
    }

    public int PaymentId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
