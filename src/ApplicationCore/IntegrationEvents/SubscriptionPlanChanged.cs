using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announced in-process after a subscription has moved (or has been scheduled to move) to another
/// plan (UC3 step 5). Publication is best-effort: a handler failure never rolls back the change.
/// </summary>
public record SubscriptionPlanChanged(
    int SubscriptionId,
    string UserName,
    string PreviousPlanHandle,
    string NewPlanHandle,
    decimal ProrationAmount,
    PlanChangeTiming Timing,
    DateTimeOffset? EffectiveAt) : INotification;
