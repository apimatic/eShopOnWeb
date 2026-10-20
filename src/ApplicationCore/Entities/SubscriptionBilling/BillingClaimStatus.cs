namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// Lifecycle of a local claim that guards a write to the billing provider.
/// </summary>
public enum BillingClaimStatus
{
    /// <summary>The claim is held and the provider call is in flight (or crashed mid-flight; see the lease).</summary>
    Pending = 0,

    /// <summary>The provider write is confirmed; the provider id is recorded on the claim.</summary>
    Completed = 1,

    /// <summary>The provider may or may not have acted; the next request for the same claim settles it.</summary>
    Unknown = 2,
}
