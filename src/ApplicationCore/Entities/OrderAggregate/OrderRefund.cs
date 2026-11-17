using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() {}

    public OrderRefund(int orderId,
        string finixTransferId,
        long amountCents,
        string idempotencyKey,
        string? status)
    {
        OrderId = orderId;
        FinixTransferId = finixTransferId;
        AmountCents = amountCents;
        IdempotencyKey = idempotencyKey;
        Status = status ?? "UNKNOWN";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string FinixTransferId { get; private set; }
    public long AmountCents { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string? status)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            Status = status;
        }
    }
}
