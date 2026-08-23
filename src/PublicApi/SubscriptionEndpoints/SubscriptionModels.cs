using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool Archived,
    bool RequestCreditCard,
    bool RequireCreditCard);

public sealed record SubscriptionDto(
    int Id,
    string? Reference,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate,
    int Interval,
    string IntervalUnit,
    string? Currency);

public sealed record CreateSubscriptionRequest(string ProductHandle);

public sealed record CreateSubscriptionResponse(SubscriptionDto Subscription, bool Created);

public sealed record BillingUser(string Id, string Email, string FirstName, string LastName);
