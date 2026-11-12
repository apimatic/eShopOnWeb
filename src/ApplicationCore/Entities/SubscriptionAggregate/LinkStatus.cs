namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum LinkStatus
{
    /// <summary>Claim taken; the billing call has not completed (or its outcome is not known yet).</summary>
    Claimed = 0,
    /// <summary>Billing system confirmed the entity; its id is stored.</summary>
    Created = 1,
    /// <summary>The billing call may have landed but could not be confirmed; settled on a later request.</summary>
    Unknown = 2
}
