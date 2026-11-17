using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class RefundRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderPaymentId { get; set; }
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string? FinixTransferId { get; set; }
    public string? FinixState { get; set; }
    public string Status { get; set; } = "Pending";
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
