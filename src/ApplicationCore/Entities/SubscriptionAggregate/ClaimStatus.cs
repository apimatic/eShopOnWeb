namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Lifecycle of a local claim taken before a billing-provider write.
/// </summary>
public enum ClaimStatus
{
    /// <summary>The claim is held; the provider write is in flight or its outcome is not yet recorded.</summary>
    Pending = 0,

    /// <summary>The provider write succeeded and its identifier is recorded on the claim.</summary>
    Completed = 1
}
