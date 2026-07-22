namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>The lifecycle transitions a subscription management surface offers.</summary>
public enum SubscriptionLifecycleAction
{
    Pause = 0,
    Resume = 1,
    Cancel = 2,
    Reactivate = 3
}

/// <summary>When a cancellation takes effect.</summary>
public enum SubscriptionCancellationTiming
{
    /// <summary>Cancel now; the subscription stops billing immediately.</summary>
    Immediate = 0,

    /// <summary>Defer the cancellation to the end of the current billing period.</summary>
    EndOfPeriod = 1
}
