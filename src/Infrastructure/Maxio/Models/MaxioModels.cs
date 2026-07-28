using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Data-transfer objects mirroring the Maxio Advanced Billing OpenAPI specification
// (maxio-spec/openapi.yaml). Only the fields consumed by this integration are declared.
// Property names are mapped to the spec's snake_case wire format via a snake_case naming
// policy configured on the JsonSerializerOptions, so [JsonPropertyName] is only used where
// the C# name would not round-trip cleanly.

/// <summary>Envelope: <c>{ "product": { ... } }</c>. Ref: Product-Response.yaml.</summary>
public class ProductEnvelope
{
    public ProductDto? Product { get; set; }
}

/// <summary>Ref: Product.yaml.</summary>
public class ProductDto
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>Whether a payment profile is required to subscribe to this product.</summary>
    public bool RequireCreditCard { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public ProductFamilyDto? ProductFamily { get; set; }
}

/// <summary>Ref: Product-Family.yaml.</summary>
public class ProductFamilyDto
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

/// <summary>Envelope: <c>{ "customer": { ... } }</c>. Ref: Customer-Response.yaml.</summary>
public class CustomerEnvelope
{
    public CustomerDto? Customer { get; set; }
}

/// <summary>Ref: Customer.yaml.</summary>
public class CustomerDto
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Request envelope for POST /customers.json. Ref: Create-Customer-Request.yaml.</summary>
public class CreateCustomerRequest
{
    public CreateCustomerRequest(CreateCustomerDto customer)
    {
        Customer = customer;
    }

    public CreateCustomerDto Customer { get; set; }
}

/// <summary>Ref: Create-Customer.yaml (subset).</summary>
public class CreateCustomerDto
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Envelope: <c>{ "subscription": { ... } }</c>. Ref: Subscription-Response.yaml.</summary>
public class SubscriptionEnvelope
{
    public SubscriptionDto? Subscription { get; set; }
}

/// <summary>Ref: Subscription.yaml (subset).</summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public ProductDto? Product { get; set; }
    public CustomerDto? Customer { get; set; }
}

/// <summary>Request envelope for POST /subscriptions.json. Ref: Create-Subscription-Request.yaml.</summary>
public class CreateSubscriptionRequest
{
    public CreateSubscriptionRequest(CreateSubscriptionDto subscription)
    {
        Subscription = subscription;
    }

    public CreateSubscriptionDto Subscription { get; set; }
}

/// <summary>Ref: Create-Subscription.yaml (subset used for a card-less enrollment).</summary>
public class CreateSubscriptionDto
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerReference { get; set; }

    /// <summary>
    /// Collection method. Ref: Collection-Method.yaml. "remittance" (invoice billing) lets
    /// the subscription activate without a stored payment profile.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>
/// Error envelope. Ref: error schemas (Error-List-Response.yaml / Customer-Error-Response.yaml).
/// The <c>errors</c> member may be a JSON array of strings or an object of field-&gt;message,
/// so it is captured loosely as a <see cref="System.Text.Json.JsonElement"/>.
/// </summary>
public class MaxioErrorEnvelope
{
    public System.Text.Json.JsonElement? Errors { get; set; }
}
