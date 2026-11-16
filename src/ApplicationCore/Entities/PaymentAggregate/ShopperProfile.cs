using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Maps the signed-in shopper to the Finix Buyer Identity that owns their payment
/// instruments. The shopper's identity is the JWT name claim.
/// </summary>
public class ShopperProfile
{
    public string BuyerId { get; set; } = string.Empty;
    public string FinixIdentityId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
