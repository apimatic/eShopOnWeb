using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionDetails(
    int Id,
    string Reference,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string Currency,
    string State,
    DateTimeOffset? NextBillingDate);
