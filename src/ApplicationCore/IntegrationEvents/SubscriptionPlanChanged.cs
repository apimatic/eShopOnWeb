using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a subscription moves to another plan (plan.md UC3, step 5).
/// </summary>
/// <remarks>Best-effort, in-process delivery only (plan.md §2.5).</remarks>
public sealed record SubscriptionPlanChanged(
    int SubscriptionId,
    string CustomerReference,
    string? PreviousPlanHandle,
    string NewPlanHandle,
    PlanChangeTiming Timing,
    decimal? PaymentDue,
    DateTimeOffset? EffectiveAt) : INotification;
