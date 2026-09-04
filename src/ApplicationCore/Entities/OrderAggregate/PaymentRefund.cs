using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund against a captured payment, keyed by a caller-supplied
/// idempotency key so repeating a request under the same key never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public const string CompletedStatus = "COMPLETED";
    public const string PendingStatus = "PENDING";
    public const string FailedStatus = "FAILED";
    public const string CancelledStatus = "CANCELLED";

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(int orderPaymentId, string idempotencyKey, decimal amount, string currency)
    {
        OrderPaymentId = orderPaymentId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = PendingStatus;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void RecordResult(string? paypalRefundId, string status)
    {
        PayPalRefundId = paypalRefundId ?? PayPalRefundId;
        Status = status;
    }
}
