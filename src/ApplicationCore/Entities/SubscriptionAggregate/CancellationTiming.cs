namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a cancellation takes effect.
/// </summary>
public enum CancellationTiming
{
    /// <summary>Cancel straight away; the subscription stops immediately.</summary>
    Immediate = 0,

    /// <summary>Cancel at the end of the current billing period; the subscription runs until then.</summary>
    EndOfPeriod
}
