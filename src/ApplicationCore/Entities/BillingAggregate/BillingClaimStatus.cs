namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Lifecycle of a local claim taken before a write to the billing provider.
/// </summary>
public enum BillingClaimStatus
{
    /// <summary>A request holds the claim and may be writing to the provider. The outcome is not yet recorded.</summary>
    Pending = 0,

    /// <summary>The provider-side record exists and its id is recorded on the claim.</summary>
    Completed = 1
}
