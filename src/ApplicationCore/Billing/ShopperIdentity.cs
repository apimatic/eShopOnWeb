namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The authenticated eShopOnWeb shopper, resolved from the PublicApi JWT.
/// </summary>
public sealed class ShopperIdentity
{
    public ShopperIdentity(string userId, string email, string userName)
    {
        UserId = userId;
        Email = email;
        UserName = userName;
    }

    public string UserId { get; }
    public string Email { get; }
    public string UserName { get; }
}
