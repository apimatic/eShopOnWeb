using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingUser(string Id, string Email);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record SubscriptionDetails(
    int Id,
    string Reference,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt,
    int? PricePointId,
    string? PricePointHandle,
    string? PricePointName);
