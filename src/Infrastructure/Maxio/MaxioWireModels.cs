using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire models that mirror the Maxio Advanced Billing OpenAPI JSON shapes (maxio-spec/openapi.yaml).
// Property names map to Maxio's snake_case fields via the shared SnakeCaseLower naming policy, so
// only irregular cases need an explicit [JsonPropertyName]. These types are internal to the
// Infrastructure layer; the application sees the projected ApplicationCore.Subscriptions models.

internal sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Organization { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

// POST /customers.json request body: { "customer": { ... } }
internal sealed class CreateCustomerRequest
{
    public CreateCustomerBody Customer { get; set; } = new();
}

internal sealed class CreateCustomerBody
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

internal sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
    public string? ProductPricePointName { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public int ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Reference { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

// POST /subscriptions.json request body: { "subscription": { ... } }
internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionBody Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionBody
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? Reference { get; set; }

    /// <summary>
    /// Collection method (Collection-Method.yaml). "remittance" makes the subscription
    /// invoice-based, so no stored payment method / card capture is required to enrol — matching
    /// the demo plans, which do not require a payment method.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
