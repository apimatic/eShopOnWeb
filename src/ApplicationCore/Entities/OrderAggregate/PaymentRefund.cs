using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(
        string payPalRefundId,
        string status,
        decimal amount,
        string currency,
        string idempotencyKey,
        DateTimeOffset createdAt)
    {
        PayPalRefundId = Guard.Against.NullOrWhiteSpace(payPalRefundId, nameof(payPalRefundId));
        Status = Guard.Against.NullOrWhiteSpace(status, nameof(status));
        Amount = amount;
        Currency = Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        IdempotencyKey = Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));
        CreatedAt = createdAt;
    }

    public int OrderPaymentId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
