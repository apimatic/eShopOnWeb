namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Lifecycle of a claim taken before a write to the billing provider.
/// </summary>
public enum BillingClaimStatus
{
    /// <summary>The claim is held and the provider write is in flight.</summary>
    Pending = 0,

    /// <summary>The provider write succeeded and its result is recorded on the claim.</summary>
    Completed = 1,

    /// <summary>The provider may or may not have acted; the next request for this claim settles it by reference.</summary>
    Unknown = 2
}
