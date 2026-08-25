using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class RefundRecord : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private RefundRecord() { }

    public RefundRecord(int paymentInfoId, string refundId, string idempotencyKey, decimal amount, string status)
    {
        PaymentInfoId = paymentInfoId;
        RefundId = refundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
        RefundDate = DateTimeOffset.UtcNow;
    }

    public int PaymentInfoId { get; private set; }
    public string RefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset RefundDate { get; private set; }
}
