using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// DTOs that mirror the Maxio Advanced Billing OpenAPI schemas (maxio-spec/openapi.yaml).
// Property names map to the spec's snake_case fields via JsonNamingPolicy.SnakeCaseLower
// configured on the client's JsonSerializerOptions. Only the fields the integration consumes
// are modeled; every request/response body is wrapped in a single top-level key per the spec.

// ---- Product family ----

public sealed record MaxioProductFamily
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Handle { get; init; }
}

public sealed record MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; init; }
}

// ---- Product (a plan) ----

public sealed record MaxioProduct
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Handle { get; init; }
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool RequireCreditCard { get; init; }
    public bool Taxable { get; init; }
    public MaxioProductFamily? ProductFamily { get; init; }
}

public sealed record MaxioProductEnvelope
{
    public MaxioProduct? Product { get; init; }
}

// ---- Customer ----

public sealed record MaxioCustomer
{
    public int Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Reference { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

public sealed record MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; init; }
}

/// <summary>Body for <c>POST /customers.json</c> (Create-Customer, wrapped in <c>customer</c>).</summary>
public sealed record MaxioCreateCustomer
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
}

public sealed record MaxioCreateCustomerEnvelope
{
    public MaxioCreateCustomer Customer { get; init; } = new();
}

// ---- Subscription ----

public sealed record MaxioSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? Currency { get; init; }
    public long ProductPriceInCents { get; init; }
    public MaxioProduct? Product { get; init; }
    public MaxioCustomer? Customer { get; init; }
}

public sealed record MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; init; }
}

/// <summary>Body for <c>POST /subscriptions.json</c> (Create-Subscription, wrapped in <c>subscription</c>).</summary>
public sealed record MaxioCreateSubscription
{
    public string ProductHandle { get; init; } = string.Empty;
    public int CustomerId { get; init; }

    /// <summary>Collection-Method; <c>remittance</c> subscribes without capturing a payment method.</summary>
    public string PaymentCollectionMethod { get; init; } = "remittance";
}

public sealed record MaxioCreateSubscriptionEnvelope
{
    public MaxioCreateSubscription Subscription { get; init; } = new();
}
