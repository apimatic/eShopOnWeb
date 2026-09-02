using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(int orderPaymentId, string payPalRefundId, decimal amount, string status, string idempotencyKey, string? note)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        OrderPaymentId = orderPaymentId;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        Note = note;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
