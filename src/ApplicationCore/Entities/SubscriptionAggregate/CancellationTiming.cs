namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>When a cancellation takes effect.</summary>
public enum CancellationTiming
{
    /// <summary>Cancel straight away; the subscription stops billing immediately.</summary>
    Immediate = 0,

    /// <summary>Cancel at the end of the current billing period; the customer keeps access until then.</summary>
    EndOfPeriod = 1
}
