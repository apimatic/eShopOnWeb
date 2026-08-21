using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionDetails(
    int Id,
    string? Reference,
    string PlanName,
    string PlanHandle,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingAt);
