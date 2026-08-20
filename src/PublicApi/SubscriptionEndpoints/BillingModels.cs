using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record BillingUser(string Id, string Email, string FirstName, string LastName);

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

public sealed record SubscriptionDto(
    int Id,
    string PlanHandle,
    string PlanName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    string? State,
    DateTimeOffset? NextBillingAt);

public sealed record SubscribeRequest(string ProductHandle);

public sealed record SubscriptionPendingDto(
    string PlanHandle,
    string Status,
    string Message);

public sealed record SubscribeResult(SubscriptionDto? Subscription, bool Created, bool IsUnknown);

internal sealed record MaxioCustomer(int Id, string Reference);

internal enum NoCardPaymentCollectionMethod
{
    Invoice,
    Remittance
}

internal sealed record MaxioProduct(
    int Id,
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    string ProductFamilyHandle,
    bool IsArchived);
