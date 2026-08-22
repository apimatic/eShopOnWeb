using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string paypalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(paypalRefundId, nameof(paypalRefundId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PayPalRefundId = paypalRefundId;
        Status = status;
        Amount = amount;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
