namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// The authenticated eShopOnWeb user, used as the Maxio customer reference.
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
