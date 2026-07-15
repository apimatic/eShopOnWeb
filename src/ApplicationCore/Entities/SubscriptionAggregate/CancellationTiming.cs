namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The two supported timings for a UC4 cancellation (see plan.md UC4 main success scenario, step 1).
/// </summary>
public enum CancellationTiming
{
    Immediate,
    EndOfPeriod
}
