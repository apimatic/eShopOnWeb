using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class PaymentRefund : BaseEntity, IAggregateRoot
{
    private PaymentRefund() { }
    public PaymentRefund(int orderId, string idempotencyKey, string paypalRefundId, decimal amount, string status)
    { OrderId = orderId; IdempotencyKey = idempotencyKey; PayPalRefundId = paypalRefundId; Amount = amount; Status = status; }
    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayPalRefundId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Status { get; private set; } = string.Empty;
}
