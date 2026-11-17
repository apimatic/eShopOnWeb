using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// One caller-requested refund. The primary key is "orderId:idempotencyKey", so a repeated
/// request under the same key cannot reach the provider a second time.
/// </summary>
public class RefundRecord
{
    public string RefundKey { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string? RefundId { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ExistingReversalIds { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
