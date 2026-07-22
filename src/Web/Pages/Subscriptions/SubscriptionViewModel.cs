using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// One subscription as shown on the management page, together with its period-to-date usage.
/// </summary>
public class SubscriptionViewModel
{
    public SubscriptionViewModel(Subscription subscription, decimal? usageBalance)
    {
        Subscription = subscription;
        UsageBalance = usageBalance;
    }

    public Subscription Subscription { get; }

    /// <summary>
    /// Units accrued this period, or <c>null</c> when the running total could not be read.
    /// </summary>
    public decimal? UsageBalance { get; }
}
