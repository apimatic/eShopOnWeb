namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionEnrollmentStatus
{
    /// <summary>Claimed; the billing calls are in flight.</summary>
    Pending = 0,

    /// <summary>The billing system holds the subscription recorded on the enrollment.</summary>
    Active = 1,

    /// <summary>A subscription create may or may not have landed; settled on the shopper's next request.</summary>
    Unknown = 2
}
