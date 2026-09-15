using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(int orderId, string buyerId, string idempotencyKey, decimal amount,
        string currency, string payPalRefundId, string payPalStatus)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Amount = amount;
        Currency = Guard.Against.NullOrEmpty(currency, nameof(currency));
        PayPalRefundId = Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        PayPalStatus = Guard.Against.NullOrEmpty(payPalStatus, nameof(payPalStatus));
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string PayPalStatus { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
