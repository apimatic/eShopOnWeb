using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing.Models;

public sealed record MaxioCustomer(
    long Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Reference);

public sealed record MaxioPlan(
    long Id,
    string? Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string? IntervalUnit,
    bool Taxable,
    bool RequireCreditCard,
    DateTimeOffset? ArchivedAt)
{
    public decimal Price => PriceInCents / 100m;
}

public sealed record MaxioSubscription(
    long Id,
    string State,
    long CustomerId,
    string? PlanHandle,
    string PlanName,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset CreatedAt,
    long BalanceInCents,
    string? PaymentCollectionMethod);
