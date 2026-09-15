using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618
    private PaymentRefund() { }
#pragma warning restore CS8618

    internal PaymentRefund(string idempotencyKey, decimal amount, string currency)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = "STARTED";
        RequestedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    internal void Complete(string payPalRefundId, string status, decimal amount, DateTimeOffset? completedAt)
    {
        if (amount != Amount)
        {
            throw new InvalidOperationException("PayPal refunded an amount different from the requested amount.");
        }
        PayPalRefundId = payPalRefundId;
        Status = status;
        CompletedAt = completedAt;
    }
}
