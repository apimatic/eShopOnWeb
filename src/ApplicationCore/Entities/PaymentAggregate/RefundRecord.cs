using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class RefundRecord : BaseEntity
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string TransferId { get; set; } = string.Empty;
    public string RefundTransferId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string State { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
