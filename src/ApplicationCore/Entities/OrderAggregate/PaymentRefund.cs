using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(string payPalRefundId, string idempotencyKey, decimal amount,
        string status, DateTimeOffset createdAt)
    {
        PayPalRefundId = Guard.Against.NullOrEmpty(payPalRefundId);
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey);
        Amount = Guard.Against.NegativeOrZero(amount);
        Status = Guard.Against.NullOrEmpty(status);
        CreatedAt = createdAt;
    }

    public int OrderId { get; private set; }
    public string PayPalRefundId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}
