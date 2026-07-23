namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>The four lifecycle transitions offered by the management surface (plan.md UC4).</summary>
public enum SubscriptionLifecycleAction
{
    Pause = 0,
    Resume = 1,

    /// <summary>Cancel straight away.</summary>
    Cancel = 2,

    /// <summary>Cancel when the current billing period ends.</summary>
    CancelAtEndOfPeriod = 3,

    Reactivate = 4
}

/// <summary>
/// A request to apply a lifecycle transition to an existing subscription.
/// </summary>
public sealed record SubscriptionLifecycleRequest
{
    public required SubscriptionLifecycleAction Action { get; init; }

    /// <summary>Optional free-text reason recorded with the transition.</summary>
    public string? Reason { get; init; }

    public static SubscriptionLifecycleRequest For(SubscriptionLifecycleAction action, string? reason = null) =>
        new() { Action = action, Reason = reason };
}
