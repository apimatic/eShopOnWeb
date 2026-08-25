using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single PayPal refund applied against a captured <see cref="Payment"/>.
/// Child entity of the Order aggregate - only created via Payment.AddRefund.
/// </summary>
public class Refund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    internal Refund(int paymentId, string payPalRefundId, decimal amount, string currencyCode, string status, string idempotencyKey, DateTimeOffset createTime)
    {
        PaymentId = paymentId;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreateTime = createTime;
    }

    public int PaymentId { get; private set; }
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    /// <summary>PayPal refund status: COMPLETED, PENDING, CANCELLED or FAILED.</summary>
    public string Status { get; private set; }
    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreateTime { get; private set; }
}
