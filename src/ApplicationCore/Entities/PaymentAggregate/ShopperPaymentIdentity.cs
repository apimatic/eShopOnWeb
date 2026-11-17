using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Maps an application shopper to the Finix buyer Identity created for that shopper.
/// The row is also the cross-request claim that prevents two concurrent identity creations.
/// </summary>
public class ShopperPaymentIdentity
{
    public string BuyerId { get; set; } = string.Empty;
    public string? FinixIdentityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
