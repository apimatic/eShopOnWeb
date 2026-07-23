namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a UC4 cancellation takes effect.
/// </summary>
public enum CancellationTiming
{
    /// <summary>Cancel now; the subscription moves to a canceled state immediately.</summary>
    Immediate = 0,

    /// <summary>Cancel at the end of the current billing period; the subscription stays active until then.</summary>
    EndOfPeriod = 1
}
