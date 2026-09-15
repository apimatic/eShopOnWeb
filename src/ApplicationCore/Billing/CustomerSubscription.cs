using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record CustomerSubscription(
    int Id,
    string State,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? ActivatedAt,
    bool AlreadyExisted);
