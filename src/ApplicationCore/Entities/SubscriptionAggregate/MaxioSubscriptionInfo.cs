using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A customer in the external billing system (Maxio Advanced Billing).
/// </summary>
public sealed record MaxioCustomer(
    int Id,
    string? Reference,
    string FirstName,
    string? LastName,
    string Email);

/// <summary>
/// A subscription in the external billing system (Maxio Advanced Billing).
/// </summary>
public sealed record MaxioSubscriptionInfo(
    int Id,
    string State,
    string PlanHandle,
    string? PlanName,
    int PriceInCents,
    DateTimeOffset? CurrentPeriodStartsAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? CanceledAt);
