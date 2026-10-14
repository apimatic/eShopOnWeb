namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Lifecycle of a local claim taken before a write is sent to the billing provider.
/// </summary>
public enum BillingClaimStatus
{
    /// <summary>The claim is held and the provider write is in flight (or was never confirmed).</summary>
    Pending = 0,

    /// <summary>The provider confirmed the write and its identifier is recorded.</summary>
    Completed = 1,

    /// <summary>The provider may have acted but the outcome could not be read back; settle it before acting again.</summary>
    Unknown = 2
}
