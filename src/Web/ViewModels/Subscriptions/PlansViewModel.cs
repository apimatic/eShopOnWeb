using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

/// <summary>What the Plans page renders: the plans on offer and the customer's current position.</summary>
public class PlansViewModel
{
    public List<SubscriptionPlan> Plans { get; set; } = new();

    /// <summary>The plan the customer is currently subscribed to, if any.</summary>
    public string? CurrentPlanHandle { get; set; }

    public bool HasActiveSubscription => !string.IsNullOrEmpty(CurrentPlanHandle);
}
