namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum EnrollmentStatus
{
    /// <summary>Claimed; the request holding the claim is talking to the billing system.</summary>
    Pending = 0,

    /// <summary>The billing system holds a live subscription for this enrollment.</summary>
    Active = 1,

    /// <summary>The create call may or may not have landed; settled by looking the subscription up by reference.</summary>
    Unknown = 2,

    /// <summary>The subscription was canceled, expired or failed; the next generation may be claimed.</summary>
    Ended = 3
}
