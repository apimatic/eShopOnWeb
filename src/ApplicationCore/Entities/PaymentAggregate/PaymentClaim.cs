using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Cross-request claim for a payment operation on an order (pay, fulfil, cancel). The composite key
/// is refused by the store when a second caller races the first.
/// </summary>
public class PaymentClaim
{
    public int OrderId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
