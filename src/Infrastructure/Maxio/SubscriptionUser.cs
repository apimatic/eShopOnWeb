namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The eShopOnWeb user a subscription operation is performed for.
/// Decoupled from the Identity model so the Maxio integration only depends
/// on the stable identifiers it needs.
/// </summary>
public class SubscriptionUser
{
    public SubscriptionUser(string userId, string userName, string email)
    {
        UserId = userId;
        UserName = userName;
        Email = email;
    }

    /// <summary>Stable identity user id (GUID). Used as the Maxio customer reference.</summary>
    public string UserId { get; }

    public string UserName { get; }

    public string Email { get; }
}

/// <summary>Outcome of a subscribe operation, including idempotency information.</summary>
public class SubscriptionEnrollment
{
    public SubscriptionEnrollment(Maxio.Models.MaxioCustomer customer,
        Maxio.Models.MaxioSubscription subscription, bool alreadySubscribed)
    {
        Customer = customer;
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public Maxio.Models.MaxioCustomer Customer { get; }

    public Maxio.Models.MaxioSubscription Subscription { get; }

    /// <summary>True when the user was already subscribed and nothing new was created.</summary>
    public bool AlreadySubscribed { get; }
}