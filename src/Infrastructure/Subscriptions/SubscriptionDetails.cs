using System;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public sealed record SubscriptionDetails(
    int? SubscriptionId,
    string Reference,
    string? PlanHandle,
    string? PlanName,
    decimal? Price,
    string? State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CreatedAt);
