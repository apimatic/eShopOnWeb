using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    internal PaymentRefund(int orderId, string idempotencyKey, string payPalRefundId, string status, decimal amount)
    {
        OrderId = orderId;
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        PayPalRefundId = Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        Amount = amount;
    }

    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
