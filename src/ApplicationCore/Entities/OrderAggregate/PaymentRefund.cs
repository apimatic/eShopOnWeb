using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    internal PaymentRefund(string paypalRefundId, string idempotencyKey, decimal amount,
        string currency, string paypalStatus, DateTimeOffset createdAt)
    {
        PaypalRefundId = Guard.Against.NullOrEmpty(paypalRefundId);
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey);
        Amount = Guard.Against.NegativeOrZero(amount);
        Currency = Guard.Against.NullOrEmpty(currency);
        PaypalStatus = Guard.Against.NullOrEmpty(paypalStatus);
        CreatedAt = createdAt;
    }

    public int OrderId { get; private set; }
    public string PaypalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string PaypalStatus { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
