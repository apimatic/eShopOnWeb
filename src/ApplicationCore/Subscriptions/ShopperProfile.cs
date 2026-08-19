namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The authenticated eShopOnWeb user that is enrolling in billing.
/// </summary>
public class ShopperProfile
{
    public ShopperProfile(string userId, string email, string userName)
    {
        UserId = userId;
        Email = email;
        UserName = userName;
    }

    public string UserId { get; }
    public string Email { get; }
    public string UserName { get; }
}
