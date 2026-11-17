using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// One refund request, claimed by (order, caller idempotency key). The composite key makes a
/// repeated request under the same key return the original refund instead of creating a second.
/// </summary>
public class RefundRecord
{
    public int OrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>The Finix reversal transfer id (the value returned as <c>refundId</c>).</summary>
    public string RefundId { get; set; } = string.Empty;
    public string CaptureTransferId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string State { get; set; } = "PENDING";
    public string? FailureMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
