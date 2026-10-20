namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingClaimAggregate;

/// <summary>
/// Lifecycle of a local claim taken before a write to the external billing system.
/// </summary>
public enum BillingClaimStatus
{
    /// <summary>The claim is held and the provider write is (or was) in flight.</summary>
    Pending = 0,

    /// <summary>The provider confirmed the write; the provider id is recorded.</summary>
    Created = 1,

    /// <summary>The write may or may not have landed at the provider; it must be settled by re-reading before anything else.</summary>
    Unknown = 2
}
