using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(
        string payPalRefundId,
        string payPalRefundStatus,
        string idempotencyKey,
        decimal amount,
        string currency)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(payPalRefundStatus, nameof(payPalRefundStatus));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalRefundId = payPalRefundId;
        PayPalRefundStatus = payPalRefundStatus;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string PayPalRefundStatus { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
