using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by EF Core
    private PaymentRefund() { }
#pragma warning restore CS8618

    internal PaymentRefund(string idempotencyKey, string paypalRequestId, decimal amount, string currency,
        DateTimeOffset now)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRequestId = paypalRequestId;
        Amount = amount;
        Currency = currency;
        Status = PaymentRefundStatus.Requested;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string PayPalRequestId { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string? PayPalStatus { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentRefundStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RecordResult(string refundId, string paypalStatus, decimal amount, DateTimeOffset now)
    {
        PayPalRefundId = refundId;
        PayPalStatus = paypalStatus;
        Amount = amount;
        Status = string.Equals(paypalStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            ? PaymentRefundStatus.Completed
            : string.Equals(paypalStatus, "PENDING", StringComparison.OrdinalIgnoreCase)
                ? PaymentRefundStatus.Pending
                : PaymentRefundStatus.Failed;
        UpdatedAt = now;
    }

    public void RecordFailure(string paypalStatus, DateTimeOffset now)
    {
        PayPalStatus = paypalStatus;
        Status = PaymentRefundStatus.Failed;
        UpdatedAt = now;
    }
}
