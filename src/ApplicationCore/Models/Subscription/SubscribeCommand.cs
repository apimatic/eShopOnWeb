namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// Command describing a request to subscribe an eShopOnWeb user to a plan.
/// </summary>
public class SubscribeCommand
{
    public SubscribeCommand(string userId, string email, string firstName, string lastName, string productHandle)
    {
        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        ProductHandle = productHandle;
    }

    /// <summary>
    /// The eShopOnWeb user id (becomes the Maxio customer reference)
    /// </summary>
    public string UserId { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// The Maxio product handle of the plan to subscribe to
    /// </summary>
    public string ProductHandle { get; }
}

/// <summary>
/// Result of subscribing a user to a plan.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(SubscriptionDto subscription, bool alreadySubscribed, int customerId)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
        CustomerId = customerId;
    }

    public SubscriptionDto Subscription { get; }

    /// <summary>
    /// True when the user was already subscribed to the plan and no new subscription was created
    /// </summary>
    public bool AlreadySubscribed { get; }

    /// <summary>
    /// The Maxio customer id backing the eShopOnWeb user
    /// </summary>
    public int CustomerId { get; }
}