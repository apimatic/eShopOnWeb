namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The authenticated eShopOnWeb user who is interacting with Maxio billing.
/// </summary>
public class Shopper
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
