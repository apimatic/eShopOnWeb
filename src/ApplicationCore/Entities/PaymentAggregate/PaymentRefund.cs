using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
    public int OrderPaymentId { get; set; }
    public string PublicId { get; set; } = Guid.NewGuid().ToString();
    public string IdempotencyKey { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string? FinixTransferId { get; set; }
    public string? FinixState { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
