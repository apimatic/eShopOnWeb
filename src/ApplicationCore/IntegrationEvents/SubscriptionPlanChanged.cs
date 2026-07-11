using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public record SubscriptionPlanChanged(
    int SubscriptionId,
    int MaxioSubscriptionId,
    string UserId,
    string OldProductHandle,
    string NewProductHandle,
    decimal OldPrice,
    decimal NewPrice,
    decimal ProratedAmount,
    DateTimeOffset EffectiveDate) : INotification;
