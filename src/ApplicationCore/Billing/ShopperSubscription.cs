using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record ShopperSubscription(
    int Id,
    string? Reference,
    string State,
    string PlanHandle,
    string PlanName,
    decimal Price,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CreatedAt);
