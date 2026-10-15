namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum EnrollmentStatus
{
    /// <summary>Claimed; the subscription create call has not completed yet.</summary>
    Pending = 0,

    /// <summary>The billing system confirmed the subscription.</summary>
    Active = 1,

    /// <summary>The create call failed in a way that leaves its outcome unknown; must be settled by reference lookup.</summary>
    Unknown = 2
}
