using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a subscription has moved to a different plan (UC3). Delivery is
/// best-effort; a failing handler never rolls the plan change back (plan.md §2.5).
/// </summary>
/// <param name="SubscriptionId">The subscription that changed plan.</param>
/// <param name="UserReference">The eShopOnWeb user (email/username) the subscription belongs to.</param>
/// <param name="FromPlanHandle">The plan the subscription was on.</param>
/// <param name="ToPlanHandle">The plan the subscription moved to.</param>
/// <param name="Timing">Whether the change applied immediately or takes effect at the next renewal.</param>
/// <param name="ProrationAmount">The prorated amount charged (positive) or credited (negative), in whole currency units.</param>
/// <param name="EffectiveAt">When the change takes effect, if known.</param>
public record SubscriptionPlanChanged(
    int SubscriptionId,
    string UserReference,
    string? FromPlanHandle,
    string ToPlanHandle,
    PlanChangeTiming Timing,
    decimal ProrationAmount,
    DateTimeOffset? EffectiveAt) : INotification;
