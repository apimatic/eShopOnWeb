using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// One refund attempt. <see cref="ClaimKey"/> is the primary key and encodes
/// <c>refund:{orderId}:{idempotencyKey}</c>, so the store itself refuses a second
/// refund under the same caller-supplied key.
/// </summary>
public class RefundRecord : IAggregateRoot
{
    public string ClaimKey { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? FinixRefundTransferId { get; set; }
    public long AmountCents { get; set; }
    public string? State { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}
