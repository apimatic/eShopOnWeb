namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The lifecycle transitions a subscription can be asked to make.
/// </summary>
public enum SubscriptionLifecycleAction
{
    Pause = 0,
    Resume = 1,

    /// <summary>Cancel straight away.</summary>
    Cancel = 2,

    /// <summary>Cancel at the end of the current billing period.</summary>
    CancelAtEndOfPeriod = 3,

    Reactivate = 4
}
