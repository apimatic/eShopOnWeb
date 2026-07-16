using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>Published after UC1 successfully enrolls a customer in a plan.</summary>
public sealed record SubscriptionActivated(
    string UserId,
    int SubscriptionId,
    string ProductHandle,
    long PriceInCents,
    DateTimeOffset? NextBillingDate) : INotification;
