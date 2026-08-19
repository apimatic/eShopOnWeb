namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The authenticated eShopOnWeb user that maps to a Maxio customer.
/// </summary>
public sealed class Shopper
{
    public Shopper(string userId, string email, string userName)
    {
        UserId = userId;
        Email = email;
        UserName = userName;
    }

    public string UserId { get; }
    public string Email { get; }
    public string UserName { get; }
}
