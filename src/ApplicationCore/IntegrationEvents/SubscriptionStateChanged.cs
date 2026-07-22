using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a subscription lifecycle transition has been applied (UC4), carrying
/// old state to new state. Delivery is best-effort; a failing handler never rolls the transition
/// back (plan.md §2.5).
/// </summary>
/// <param name="SubscriptionId">The subscription that changed state.</param>
/// <param name="UserReference">The eShopOnWeb user (email/username) the subscription belongs to.</param>
/// <param name="Action">The lifecycle action that was applied.</param>
/// <param name="OldState">The state the subscription was in before the transition.</param>
/// <param name="NewState">The state the provider reports after the transition.</param>
/// <param name="EffectiveAt">When the transition takes effect, if known. Deferred for an end-of-period cancellation.</param>
public record SubscriptionStateChanged(
    int SubscriptionId,
    string UserReference,
    SubscriptionLifecycleAction Action,
    SubscriptionState OldState,
    SubscriptionState NewState,
    DateTimeOffset? EffectiveAt) : INotification;
