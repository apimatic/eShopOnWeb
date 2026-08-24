namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The authenticated shopper's identity, as known to eShopOnWeb, used to locate or
/// provision the corresponding customer record at the billing provider.
/// </summary>
public class ShopperIdentity
{
    public ShopperIdentity(string username, string email, string firstName, string lastName)
    {
        Username = username;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable, unique user identity; used as the provider-side customer reference.</summary>
    public string Username { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
}
