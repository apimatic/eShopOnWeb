using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire-level DTOs mirroring the Maxio OpenAPI spec (maxio-spec/openapi.yaml).
// Property names are PascalCase and mapped to the spec's snake_case JSON via a
// snake_case naming policy configured on the serializer. Only the fields the
// integration consumes are modelled; unknown fields are ignored on deserialization.

/// <summary>Envelope for a single product: <c>{ "product": { ... } }</c>.</summary>
internal sealed class ProductEnvelope
{
    public ProductWire? Product { get; set; }
}

/// <summary>Maxio "product" — surfaced to the app as a subscription plan.</summary>
internal sealed class ProductWire
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public ProductFamilyWire? ProductFamily { get; set; }
}

internal sealed class ProductFamilyWire
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
}

/// <summary>Envelope for a single customer: <c>{ "customer": { ... } }</c>.</summary>
internal sealed class CustomerEnvelope
{
    public CustomerWire? Customer { get; set; }
}

internal sealed class CustomerWire
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

/// <summary>Request body for POST /customers.json.</summary>
internal sealed class CreateCustomerBody
{
    public CustomerAttributesWire Customer { get; set; } = new();
}

internal sealed class CustomerAttributesWire
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Envelope for a single subscription: <c>{ "subscription": { ... } }</c>.</summary>
internal sealed class SubscriptionEnvelope
{
    public SubscriptionWire? Subscription { get; set; }
}

internal sealed class SubscriptionWire
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ProductWire? Product { get; set; }
    public CustomerWire? Customer { get; set; }
}

/// <summary>Request body for POST /subscriptions.json.</summary>
internal sealed class CreateSubscriptionBody
{
    public CreateSubscriptionWire Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionWire
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }

    /// <summary>
    /// Collection method (spec enum: automatic | remittance | prepaid | invoice). "remittance"
    /// enrolls via invoice billing so no stored payment method is required at signup.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>Maxio error envelope. The <c>errors</c> field may be a list or an object per the spec.</summary>
internal sealed class MaxioErrorEnvelope
{
    public List<string>? Errors { get; set; }
}
