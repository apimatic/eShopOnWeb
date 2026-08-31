using System;
namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }
    public PaymentRefund(string key, string id, string status, decimal amount, DateTimeOffset at) { IdempotencyKey = key; RefundId = id; Status = status; Amount = amount; CreatedAt = at; }
    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RefundId { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
