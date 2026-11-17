using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A refund (Finix transfer reversal) recorded against an order.
/// </summary>
public class RefundRecord
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
