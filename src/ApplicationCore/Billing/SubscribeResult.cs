namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Outcome of a subscribe call. <see cref="Created"/> distinguishes a brand-new enrollment
/// from an idempotent replay (the user already had an active subscription to the plan).
/// </summary>
public class SubscribeResult
{
    public required CustomerSubscription Subscription { get; init; }

    /// <summary>True when a new subscription was created; false when an equivalent active one already existed.</summary>
    public bool Created { get; init; }
}
