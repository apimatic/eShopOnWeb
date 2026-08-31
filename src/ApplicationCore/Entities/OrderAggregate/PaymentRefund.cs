using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string paypalRefundId, string status, decimal amount,
        decimal? paypalFee, decimal? netAmount, DateTimeOffset createdAt)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = paypalRefundId;
        Status = status;
        Amount = amount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CreatedAt = createdAt;
    }

    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
