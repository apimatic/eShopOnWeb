namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public enum SubscriptionSignupStatus
{
    /// <summary>The requested plan handle is not part of the configured product family.</summary>
    PlanNotFound,
    /// <summary>The customer already holds a live subscription to the plan; nothing new was created.</summary>
    AlreadySubscribed,
    /// <summary>A new subscription was created in Maxio.</summary>
    Created
}

public class SubscriptionSignupResult
{
    public SubscriptionSignupStatus Status { get; set; }
    public long CustomerId { get; set; }
    public CustomerSubscription? Subscription { get; set; }
}
