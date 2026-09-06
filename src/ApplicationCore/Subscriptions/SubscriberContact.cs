namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Contact details for an eShopOnWeb user, as held by the identity store.</summary>
public class SubscriberContact
{
    public SubscriberContact(string userId, string userName, string email)
    {
        UserId = userId;
        UserName = userName;
        Email = email;
    }

    public string UserId { get; }

    public string UserName { get; }

    public string Email { get; }
}
