using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

public sealed record ShopperSubscription(
    int Id,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingDate);
