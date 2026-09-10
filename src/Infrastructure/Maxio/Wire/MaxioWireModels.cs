using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

// Wire models mirroring the Maxio Advanced Billing OpenAPI schemas. JSON property names are
// snake_case, applied globally via JsonNamingPolicy.SnakeCaseLower, so these keep C# PascalCase.
// Only the fields eShopOnWeb needs are modelled; unknown fields are ignored on deserialization.

/// <summary>Mirrors the <c>Customer</c> schema (the subset eShopOnWeb reads).</summary>
public sealed record MaxioCustomer
{
    public long Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Reference { get; init; }
}

/// <summary>Mirrors the <c>Customer-Response</c> envelope.</summary>
public sealed record MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; init; }
}

/// <summary>Mirrors the <c>Create-Customer</c> request body.</summary>
public sealed record MaxioCreateCustomer
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Reference { get; init; }
}

/// <summary>Mirrors the <c>Create-Customer-Request</c> envelope.</summary>
public sealed record MaxioCreateCustomerRequest
{
    public required MaxioCreateCustomer Customer { get; init; }
}

/// <summary>Mirrors the <c>Product</c> schema (the subset eShopOnWeb reads).</summary>
public sealed record MaxioProduct
{
    public long Id { get; init; }
    public string? Name { get; init; }
    public string? Handle { get; init; }
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public bool RequireCreditCard { get; init; }
}

/// <summary>Mirrors the <c>Product-Response</c> envelope.</summary>
public sealed record MaxioProductEnvelope
{
    public MaxioProduct? Product { get; init; }
}

/// <summary>Mirrors the <c>Subscription</c> schema (the subset eShopOnWeb reads).</summary>
public sealed record MaxioSubscription
{
    public long Id { get; init; }
    public string? State { get; init; }
    public long ProductPriceInCents { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? PaymentCollectionMethod { get; init; }
    public MaxioProduct? Product { get; init; }
}

/// <summary>Mirrors the <c>Subscription-Response</c> envelope.</summary>
public sealed record MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; init; }
}

/// <summary>Mirrors the <c>Create-Subscription</c> request body (the subset eShopOnWeb sends).</summary>
public sealed record MaxioCreateSubscription
{
    public required string ProductHandle { get; init; }
    public long CustomerId { get; init; }
    public string? PaymentCollectionMethod { get; init; }
}

/// <summary>Mirrors the <c>Create-Subscription-Request</c> envelope.</summary>
public sealed record MaxioCreateSubscriptionRequest
{
    public required MaxioCreateSubscription Subscription { get; init; }
}
