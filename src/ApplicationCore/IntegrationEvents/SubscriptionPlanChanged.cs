using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a subscription moved between plans (UC3, step 5). Published best-effort after
/// the provider call succeeds.
/// </summary>
/// <param name="UserName">The eShopOnWeb user reference the subscription belongs to, if known.</param>
/// <param name="PreviousPlanHandle">The plan the subscription was on before the change.</param>
/// <param name="Timing">Whether the change applied immediately or was deferred to renewal.</param>
/// <param name="Subscription">The subscription as the provider reported it after the change.</param>
public record SubscriptionPlanChanged(
    string? UserName,
    string PreviousPlanHandle,
    PlanChangeTiming Timing,
    Subscription Subscription) : INotification;
