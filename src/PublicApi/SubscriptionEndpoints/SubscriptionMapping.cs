using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the billing domain models onto the API's wire shapes.
/// </summary>
/// <remarks>
/// Written by hand rather than via <see cref="MappingProfile"/>: these types cross a trust boundary, and
/// keeping the projection explicit means adding a field to the domain model never silently publishes it.
/// </remarks>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Currency = plan.Currency,
        IntervalLength = plan.IntervalLength,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        IsLive = subscription.IsLive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        Currency = subscription.Currency,
        IntervalLength = subscription.IntervalLength,
        IntervalUnit = subscription.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        BillingCustomerId = subscription.BillingCustomerId,

        // subscription.Reference is deliberately not published: it is an internal idempotency handle, and
        // exposing it would invite callers to depend on a shape we need to stay free to change.
    };
}
