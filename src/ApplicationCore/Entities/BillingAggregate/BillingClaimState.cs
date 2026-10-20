namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Lifecycle of a billing claim row (a customer link or a subscription enrollment).
/// </summary>
public enum BillingClaimState
{
    /// <summary>Claimed by a request that is talking to the billing provider right now.</summary>
    Pending = 0,

    /// <summary>The provider-side record exists and its id is stored on the row.</summary>
    Completed = 1,

    /// <summary>A write may or may not have landed at the provider; the next request for the same key settles it.</summary>
    Unknown = 2
}
