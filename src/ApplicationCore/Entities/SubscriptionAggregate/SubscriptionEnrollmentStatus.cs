namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionEnrollmentStatus
{
    /// <summary>A subscribe attempt holds the claim and is talking to the billing provider.</summary>
    Pending = 0,

    /// <summary>The billing provider confirmed the subscription.</summary>
    Active = 1,

    /// <summary>The create call failed in transit; the provider may have acted. Settled by re-reading on the next request.</summary>
    Unknown = 2
}
