using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announced in-process after a lifecycle transition succeeded (UC4 step 3), carrying old state to
/// new state. Publication is best-effort: a handler failure never rolls back the transition.
/// </summary>
public record SubscriptionStateChanged(
    int SubscriptionId,
    string? UserName,
    SubscriptionLifecycleAction Action,
    SubscriptionLifecycleState PreviousState,
    SubscriptionLifecycleState NewState,
    DateTimeOffset? EffectiveAt) : INotification;
