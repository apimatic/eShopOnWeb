using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string PayPalRefundId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(int orderPaymentId, string idempotencyKey, string payPalRefundId, string status,
        decimal amount, string currency, string? note, DateTimeOffset createdAt)
    {
        OrderPaymentId = orderPaymentId;
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        Currency = currency;
        Note = note;
        CreatedAt = createdAt;
    }

    public bool IsCompleted => Status == "COMPLETED";
    public bool IsPendingOrCompleted => Status is "COMPLETED" or "PENDING";
}
