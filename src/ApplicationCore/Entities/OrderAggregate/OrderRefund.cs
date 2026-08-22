using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
#pragma warning disable CS8618
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string paypalRefundId, string status, decimal amount, string idempotencyKey)
    {
        PayPalRefundId = paypalRefundId;
        Status = status;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
    }

    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string IdempotencyKey { get; private set; }

    public bool CountsAgainstRemaining
    {
        get
        {
            if (string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }
}
