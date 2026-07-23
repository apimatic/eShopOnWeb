using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a lifecycle transition is applied, carrying old to new state
/// (plan.md UC4, step 3).
/// </summary>
/// <remarks>Best-effort, in-process delivery only (plan.md §2.5).</remarks>
public sealed record SubscriptionStateChanged(
    int SubscriptionId,
    string CustomerReference,
    SubscriptionState PreviousState,
    SubscriptionState NewState,
    SubscriptionLifecycleAction Action,
    DateTimeOffset? EffectiveAt) : INotification;
