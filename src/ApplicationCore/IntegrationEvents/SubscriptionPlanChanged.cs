using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after UC3 commits a plan change.</summary>
public sealed record SubscriptionPlanChanged(
    string UserId,
    int SubscriptionId,
    string OldProductHandle,
    string NewProductHandle,
    long ProratedAdjustmentInCents,
    DateTimeOffset EffectiveAt) : INotification;
