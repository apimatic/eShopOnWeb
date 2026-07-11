using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public record SubscriptionStateChanged(
    int SubscriptionId,
    int MaxioSubscriptionId,
    string UserId,
    string OldState,
    string NewState,
    DateTimeOffset EffectiveDate,
    string? Reason = null) : INotification;
