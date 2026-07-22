namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a cancellation takes effect.
/// </summary>
public enum CancellationTiming
{
    /// <summary>Cancel straight away; the subscription stops being active now.</summary>
    Immediate = 0,

    /// <summary>Cancel at the end of the current paid period; the subscription stays active until then.</summary>
    EndOfPeriod = 1
}
