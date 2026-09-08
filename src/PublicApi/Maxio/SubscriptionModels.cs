namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Identity information for the signed-in eShopOnWeb shopper that is being
/// enrolled as a Billing customer. The email address doubles as the Billing
/// customer reference so that enrollment is stable across app restarts.
/// </summary>
public sealed class SubscriberProfile
{
    public SubscriberProfile(string email, string? firstName, string? lastName)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}

/// <summary>
/// Outcome of a subscribe attempt. <see cref="AlreadyExisted"/> is true when the
/// subscriber already had a live subscription to the requested plan and the
/// existing subscription was returned instead of creating a duplicate.
/// </summary>
public sealed class SubscriptionEnrollment
{
    public SubscriptionEnrollment(MaxioSubscription subscription, bool alreadyExisted)
    {
        Subscription = subscription;
        AlreadyExisted = alreadyExisted;
    }

    public MaxioSubscription Subscription { get; }

    public bool AlreadyExisted { get; }
}
