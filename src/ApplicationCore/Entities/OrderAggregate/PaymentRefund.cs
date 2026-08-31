using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(string callerIdempotencyKey, string paypalRequestId, string paypalRefundId,
        string status, decimal amount, string currency, DateTimeOffset createdAt)
    {
        CallerIdempotencyKey = callerIdempotencyKey;
        PayPalRequestId = paypalRequestId;
        PayPalRefundId = paypalRefundId;
        Status = status;
        Amount = amount;
        Currency = currency;
        CreatedAt = createdAt;
    }

    public int OrderId { get; private set; }
    public string CallerIdempotencyKey { get; private set; } = string.Empty;
    public string PayPalRequestId { get; private set; } = string.Empty;
    public string PayPalRefundId { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
