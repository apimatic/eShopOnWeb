namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum EnrollmentStatus
{
    /// <summary>Claimed locally; the subscription may or may not exist in the billing system yet.</summary>
    Pending = 0,
    /// <summary>The billing system confirmed the subscription.</summary>
    Completed = 1
}
