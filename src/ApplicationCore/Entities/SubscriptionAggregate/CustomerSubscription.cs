using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record CustomerSubscription(
    int Id,
    string State,
    string ProductHandle,
    string ProductName,
    decimal Price,
    DateTimeOffset? NextBillingDate,
    bool AlreadyExisted = false);
