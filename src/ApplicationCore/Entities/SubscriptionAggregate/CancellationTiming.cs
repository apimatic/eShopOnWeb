namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a cancellation takes effect (UC4).
/// </summary>
public enum CancellationTiming
{
    /// <summary>Cancel straight away; the subscription stops billing now.</summary>
    Immediate = 0,

    /// <summary>Cancel at the end of the current billing period.</summary>
    EndOfPeriod
}
