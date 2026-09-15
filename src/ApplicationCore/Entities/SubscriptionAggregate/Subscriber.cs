namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The eShopOnWeb identity used to find or create a Maxio customer.
/// </summary>
public class Subscriber
{
    public Subscriber(string userId, string userName, string email)
    {
        UserId = userId;
        UserName = userName;
        Email = email;
    }

    /// <summary>ASP.NET Identity user id — used as the Maxio customer reference.</summary>
    public string UserId { get; }

    public string UserName { get; }

    public string Email { get; }
}
