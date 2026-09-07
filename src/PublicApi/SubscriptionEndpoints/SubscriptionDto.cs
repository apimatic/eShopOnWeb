using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscriptionDto(
    int SubscriptionId,
    string ProductHandle,
    string ProductName,
    DateTimeOffset? NextBillingAt,
    string State
);
