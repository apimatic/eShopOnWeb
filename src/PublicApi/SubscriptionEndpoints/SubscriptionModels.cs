using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record SubscriptionDto(
    int MaxioSubscriptionId,
    string Reference,
    string PlanHandle,
    string PlanName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt);

public sealed record CreateSubscriptionRequest(string ProductHandle);

public sealed record BillingUser(string Id, string Email, string FirstName, string LastName);

internal sealed record MaxioCustomer(int Id, string Reference);
