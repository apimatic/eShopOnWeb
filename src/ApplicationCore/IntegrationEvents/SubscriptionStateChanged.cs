using System;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after UC4 applies a lifecycle transition (pause/resume/cancel/reactivate).</summary>
public sealed record SubscriptionStateChanged(
    string UserId,
    int SubscriptionId,
    SubscriptionState OldState,
    SubscriptionState NewState,
    DateTimeOffset EffectiveAt) : INotification;
