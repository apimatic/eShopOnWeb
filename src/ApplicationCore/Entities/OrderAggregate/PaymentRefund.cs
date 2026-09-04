using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A refund against a captured payment. The <see cref="IdempotencyKey"/> is the
/// caller-supplied key used as PayPal's PayPal-Request-Id so a repeated request under
/// the same key never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public int OrderPaymentId { get; private set; }
    public OrderPayment? OrderPayment { get; private set; }

    public string PayPalRefundId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(int orderPaymentId, string payPalRefundId, decimal amount, string currency,
        string idempotencyKey, string status)
    {
        OrderPaymentId = orderPaymentId;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
        Status = status;
    }
}