namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>When a cancellation (UC4) takes effect.</summary>
public enum CancellationTiming
{
    /// <summary>Cancel now; the subscription moves to <see cref="SubscriptionState.Canceled"/> immediately.</summary>
    Immediate = 0,

    /// <summary>Cancel at the end of the current billing period; the subscription stays live until then.</summary>
    EndOfPeriod = 1
}
