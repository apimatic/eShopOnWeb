using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    int Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    string Currency,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record SubscriptionDto(
    int Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    decimal Price,
    string Currency,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed record MaxioProduct(
    int Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequireCreditCard,
    DateTimeOffset? ArchivedAt,
    string ProductFamilyHandle);

public sealed record MaxioCustomer(int Id, string? Reference);

public sealed record MaxioSite(string Currency, bool RelationshipInvoicingEnabled, bool Test);

public sealed record MaxioSubscription(
    int Id,
    string State,
    long ProductPriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    string? Reference,
    string Currency,
    MaxioCustomer Customer,
    MaxioProduct Product);

public sealed record CreateMaxioCustomer(
    string FirstName,
    string LastName,
    string Email,
    string Reference);

public sealed record CreateMaxioSubscription(
    string ProductHandle,
    int CustomerId,
    string Reference,
    string PaymentCollectionMethod);
