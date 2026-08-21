using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed class ShopperSubscription
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string State { get; init; } = string.Empty;
    public int ProductPriceInCents { get; init; }
    public decimal Price => ProductPriceInCents / 100m;
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextBillingDate => NextAssessmentAt ?? CurrentPeriodEndsAt;
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public int? CustomerId { get; init; }

    /// <summary>
    /// True when the subscription is not in an end-of-life state defined by the Maxio spec.
    /// </summary>
    public bool IsCurrent
    {
        get
        {
            return State is not (
                "canceled" or
                "expired" or
                "failed_to_create" or
                "trial_ended");
        }
    }
}
