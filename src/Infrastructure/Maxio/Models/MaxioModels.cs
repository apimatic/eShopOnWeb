using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Wire models mapping to the Maxio Advanced Billing OpenAPI schemas (maxio-spec/openapi.yaml).
// Property names are PascalCase; the HttpClient uses a snake_case naming policy so they bind to the
// snake_case JSON fields defined by the spec. Only the fields consumed by this integration are modeled.

/// <summary>Envelope for a single product: <c>{ "product": { ... } }</c>. See schema Product-Response.</summary>
public record MaxioProductEnvelope(MaxioProduct Product);

/// <summary>Maxio Product (a subscription plan). See schema Product.</summary>
public record MaxioProduct(
    int Id,
    string? Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    MaxioProductFamily? ProductFamily);

/// <summary>Nested product family reference. See schema Product-Family.</summary>
public record MaxioProductFamily(int Id, string? Handle, string? Name);

/// <summary>Envelope for a single customer: <c>{ "customer": { ... } }</c>. See schema Customer-Response.</summary>
public record MaxioCustomerEnvelope(MaxioCustomer Customer);

/// <summary>Maxio Customer. See schema Customer.</summary>
public record MaxioCustomer(
    int Id,
    string? Reference,
    string? FirstName,
    string? LastName,
    string? Email);

/// <summary>Envelope for a single subscription: <c>{ "subscription": { ... } }</c>. See schema Subscription-Response.</summary>
public record MaxioSubscriptionEnvelope(MaxioSubscription Subscription);

/// <summary>Maxio Subscription. See schema Subscription (only consumed fields are modeled).</summary>
public record MaxioSubscription(
    int Id,
    string State,
    long ProductPriceInCents,
    string? PaymentCollectionMethod,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? CreatedAt,
    MaxioProduct? Product,
    MaxioCustomer? Customer);
