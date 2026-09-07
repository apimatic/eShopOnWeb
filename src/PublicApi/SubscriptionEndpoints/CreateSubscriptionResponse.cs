using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record CreateSubscriptionResponse(
    int SubscriptionId,
    string State,
    DateTimeOffset? NextBillingAt
);
