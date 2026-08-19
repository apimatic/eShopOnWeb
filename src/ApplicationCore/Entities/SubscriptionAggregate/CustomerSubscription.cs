using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record CustomerSubscription(
    int Id,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    decimal Price,
    string State,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt,
    string? Reference);
