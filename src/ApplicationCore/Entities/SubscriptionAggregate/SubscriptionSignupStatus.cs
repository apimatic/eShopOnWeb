namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionSignupStatus
{
    /// <summary>Claimed; billing calls not finished yet.</summary>
    Pending = 0,

    /// <summary>A write's outcome is unknown; it is settled later with the same reference and token.</summary>
    Unknown = 1,

    /// <summary>The billing system asks the customer to complete an action (3-D Secure) first.</summary>
    ActionRequired = 2,

    /// <summary>The subscription exists in the billing system.</summary>
    Completed = 3,

    /// <summary>The billing system rejected the signup; nothing was created.</summary>
    Rejected = 4,

    /// <summary>The subscription this signup made has ended (canceled, expired, ...).</summary>
    Ended = 5,
}
