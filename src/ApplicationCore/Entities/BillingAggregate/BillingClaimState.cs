namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Lifecycle of a local claim taken before a write to the billing provider.
/// </summary>
public enum BillingClaimState
{
    /// <summary>The claim is held and the provider write is (or may be) in flight.</summary>
    Pending = 0,

    /// <summary>The provider write succeeded and its identifier is recorded.</summary>
    Active = 1,

    /// <summary>The provider write may or may not have landed; it must be settled by re-reading the provider.</summary>
    Unknown = 2
}
