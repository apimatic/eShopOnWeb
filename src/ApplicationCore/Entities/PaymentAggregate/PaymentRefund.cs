using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
    public const string RefundStatusFailed = "FAILED";

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() {}

    public PaymentRefund(int paymentId, string payPalRefundId, string idempotencyKey, decimal amount, string currency, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PaymentId = paymentId;
        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
