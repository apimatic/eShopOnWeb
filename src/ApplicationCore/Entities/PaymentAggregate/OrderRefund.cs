using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A refund request against a captured order. The composite key (OrderId, IdempotencyKey) is the
/// cross-request claim that makes a repeated caller key a no-op.
/// </summary>
public class OrderRefund
{
    public int OrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RefundId { get; set; } = string.Empty;
    public string? FinixReversalId { get; set; }
    public string? State { get; set; }
    public long AmountCents { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
