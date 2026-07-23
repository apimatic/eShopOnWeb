namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a cancellation takes effect.
/// </summary>
public enum CancellationTiming
{
    /// <summary>Cancel the subscription right now.</summary>
    Immediate = 0,

    /// <summary>Let the subscription run to the end of the paid period, then cancel.</summary>
    EndOfPeriod
}
