using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Serializes a single money-moving operation (capture or void) per order. The primary key is
/// "orderId:OPERATION", so a concurrent second caller is refused by the database.
/// </summary>
public class OrderOperationClaim
{
    public string ClaimKey { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
