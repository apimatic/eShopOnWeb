using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

/// <summary>Links a shop account (by e-mail) to its customer record at Stax.</summary>
public class ShopperProfile : BaseEntity
{
    public string Email { get; private set; }
    public string StaxCustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private ShopperProfile() { }

    public ShopperProfile(string email, string staxCustomerId)
    {
        Email = email;
        StaxCustomerId = staxCustomerId;
    }
}
