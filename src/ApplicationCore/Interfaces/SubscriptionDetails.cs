using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionDetails(
    int Id,
    string? Reference,
    string PlanHandle,
    string PlanName,
    decimal PriceAmount,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingDate);
