namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Identity of an eShopOnWeb shopper used to find or create the matching Maxio customer.
/// </summary>
public sealed class ShopperBillingProfile
{
    public ShopperBillingProfile(string id, string email, string? userName)
    {
        Id = id;
        Email = email;
        UserName = userName;
    }

    public string Id { get; }
    public string Email { get; }
    public string? UserName { get; }
}
