using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A write-ahead claim keyed by the caller's refund idempotency key. The composite key
/// is (OrderId, IdempotencyKey); two distinct partial refunds carry distinct keys.
/// </summary>
public class RefundClaim
{
    public int OrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
