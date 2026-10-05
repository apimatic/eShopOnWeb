using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Request/response shapes for the Maxio Advanced Billing API. These mirror
// the components/schemas of the authoritative OpenAPI specification
// (maxio-spec/openapi.yaml): Customers, Products, and Subscriptions.
// Serialization uses a snake_case naming policy to match the wire format.

public sealed class CustomerWrapper
{
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ProductWrapper
{
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class SubscriptionWrapper
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// Body for POST /subscriptions.json per the spec's Create-Subscription schema.
/// Only the subset of fields used by this integration is modeled.
/// </summary>
public sealed class CreateMaxioSubscriptionPayload
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// Spec Collection-Method: "remittance" signs the customer up without
    /// collecting a payment method (the demo products do not require one);
    /// "automatic" would attempt payment at signup.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

public sealed class CreateMaxioSubscriptionRequest
{
    public CreateMaxioSubscriptionPayload Subscription { get; set; } = new();
}

/// <summary>
/// Body for POST /customers.json per the spec's Create-Customer schema.
/// first_name, last_name and email are required by the spec.
/// </summary>
public sealed class CreateMaxioCustomerRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public sealed class CreateMaxioCustomerPayload
{
    public CreateMaxioCustomerRequest Customer { get; set; } = new();
}