using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public record CustomerSubscription(
    string PlanHandle,
    string PlanName,
    long? PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate);
