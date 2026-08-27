using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
    public const string PendingStatus = "PENDING";
    public const string FailedStatus = "FAILED";

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() {}

    public PaymentRefund(int paymentId, string idempotencyKey, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        PaymentId = paymentId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
    }

    public int PaymentId { get; private set; }

    /// <summary>Caller-supplied key; repeating a request under the same key must not refund twice.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = PendingStatus;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void MarkCompleted(string payPalRefundId, string status)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
    }

    public void MarkFailed()
    {
        Status = FailedStatus;
    }
}
