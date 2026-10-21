namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Lifecycle of a local claim that guards a write to the billing provider.
/// </summary>
public enum BillingClaimStatus
{
    /// <summary>The claim is held and the provider write has not been confirmed yet.</summary>
    Pending = 0,

    /// <summary>The provider write is confirmed and its provider id is recorded.</summary>
    Completed = 1
}
