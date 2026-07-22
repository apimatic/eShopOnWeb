using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

/// <summary>What the subscription management page renders.</summary>
public class MySubscriptionsViewModel
{
    public List<CustomerSubscription> Subscriptions { get; set; } = new();

    public List<SubscriptionPlan> Plans { get; set; } = new();

    /// <summary>The metered add-on usage accrues against, when it is configured and available.</summary>
    public MeteredComponent? MeteredComponent { get; set; }

    /// <summary>Running period-to-date usage on the active subscription, when it could be read.</summary>
    public int? PeriodToDateUnits { get; set; }

    /// <summary>A plan-change preview awaiting the customer's confirmation.</summary>
    public PlanChangePreview? PendingPreview { get; set; }

    public CustomerSubscription? ActiveSubscription =>
        Subscriptions.FirstOrDefault(s => s.IsBillable);

    /// <summary>Period-to-date charge in dollars, when both the balance and the unit price are known.</summary>
    public decimal? PeriodToDateCharge =>
        PeriodToDateUnits.HasValue && MeteredComponent is not null
            ? PeriodToDateUnits.Value * MeteredComponent.UnitPrice
            : null;
}
