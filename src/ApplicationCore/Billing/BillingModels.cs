using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record BillingCustomer(long Id, string Reference);

public sealed record NewBillingCustomer(
    string FirstName,
    string LastName,
    string Email,
    string Reference);

public sealed record BillingSubscription(
    long Id,
    string? Reference,
    string State,
    string ProductHandle,
    string ProductName,
    string ProductFamilyHandle,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string? Currency,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt)
{
    public DateTimeOffset? NextBillingAt => NextAssessmentAt ?? CurrentPeriodEndsAt;
}

public sealed record ShopperIdentity(string UserId, string Email);
