namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The authenticated eShopOnWeb shopper, mapped 1:1 onto a Maxio customer via <see cref="UserId"/> as the customer reference.
/// </summary>
public sealed class ShopperIdentity
{
    public ShopperIdentity(string userId, string email, string? userName)
    {
        UserId = userId;
        Email = email;
        UserName = userName;
    }

    public string UserId { get; }
    public string Email { get; }
    public string? UserName { get; }
}
