namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// When a cancellation takes effect (UC4).
/// </summary>
public enum CancellationTiming
{
    /// <summary>Cancel straight away.</summary>
    Immediate = 0,

    /// <summary>Cancel when the current billing period ends.</summary>
    EndOfPeriod = 1
}
