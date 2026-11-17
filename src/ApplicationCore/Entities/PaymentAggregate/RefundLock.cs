using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Serializes refunds per order so two different partial refunds cannot both pass the
/// "not more than captured" check and over-refund the capture.
/// </summary>
public class RefundLock
{
    public int OrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
