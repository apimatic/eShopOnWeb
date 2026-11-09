using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

/// <summary>
/// Links a shop account (by e-mail, the JWT's name claim) to its customer record at Stax.
/// </summary>
public class ShopperProfile : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
