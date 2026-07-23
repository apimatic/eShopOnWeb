namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a cancellation takes effect.
/// </summary>
public enum CancellationTiming
{
    /// <summary>Cancel now; the subscription stops billing immediately.</summary>
    Immediately = 0,

    /// <summary>Cancel at the end of the current billing period, which the customer has already paid for.</summary>
    EndOfPeriod
}
