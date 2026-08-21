using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string? PricePointHandle);

public sealed record SubscriptionDto(
    int Id,
    string PlanHandle,
    string PlanName,
    long? PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed record SubscriptionCreationResult(SubscriptionDto Subscription, bool Created);

public sealed record CurrentBillingUser(
    string UserKey,
    string Email,
    string FirstName,
    string LastName,
    string CustomerReference);

public sealed record MaxioProduct(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    string? PricePointHandle);

public sealed record MaxioCustomer(int Id, string Reference);

public sealed record MaxioSubscription(
    int Id,
    string? Reference,
    string PlanHandle,
    string PlanName,
    string? ProductFamilyHandle,
    long? PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt)
{
    public SubscriptionDto ToDto() => new(
        Id,
        PlanHandle,
        PlanName,
        PriceInCents,
        Currency,
        State,
        NextBillingAt);
}

public sealed class SubscriptionProblem
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
