using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire shapes for the Maxio Advanced Billing API. Property names are serialized in
// snake_case via JsonNamingPolicy.SnakeCaseLower (see MaxioGateway.JsonOptions).

public record MaxioCustomer(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    string? Reference);

public record MaxioProduct(
    int Id,
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequireCreditCard,
    DateTimeOffset? ArchivedAt);

public record MaxioSubscription(
    int Id,
    string State,
    int ProductPriceInCents,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CurrentPeriodStartedAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? CanceledAt,
    string? PaymentCollectionMethod,
    MaxioProductSummary? Product)
{
    public string? PlanHandle => Product?.Handle;
    public string? PlanName => Product?.Name;
}

public record MaxioProductSummary(string Handle, string Name);

public record CreateCustomerRequest([property: JsonPropertyName("customer")] CreateCustomerBody Customer);

public record CreateCustomerBody(
    string FirstName,
    string LastName,
    string Email,
    string Reference);

public record CreateSubscriptionRequest([property: JsonPropertyName("subscription")] CreateSubscriptionBody Subscription);

public record CreateSubscriptionBody(
    string ProductHandle,
    int CustomerId,
    string PaymentCollectionMethod);
