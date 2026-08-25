using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund
{
    public string IdempotencyKey { get; set; } = "";
    public string RefundId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
