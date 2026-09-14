namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe attempt. <see cref="AlreadyExisted"/> is <c>true</c> when the user
/// was already enrolled in the requested plan and the existing subscription was returned instead
/// of creating a duplicate (e.g. a double-click).
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(CustomerSubscription subscription, int customerId, string customerReference, bool alreadyExisted)
    {
        Subscription = subscription;
        CustomerId = customerId;
        CustomerReference = customerReference;
        AlreadyExisted = alreadyExisted;
    }

    /// <summary>The active subscription, whether newly created or pre-existing.</summary>
    public CustomerSubscription Subscription { get; }

    /// <summary>The Maxio customer id backing the eShopOnWeb user.</summary>
    public int CustomerId { get; }

    /// <summary>The Maxio customer reference (the eShopOnWeb user identity).</summary>
    public string CustomerReference { get; }

    /// <summary>True when an existing subscription was returned rather than a new one being created.</summary>
    public bool AlreadyExisted { get; }
}
