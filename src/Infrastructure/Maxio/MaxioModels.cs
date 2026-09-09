using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Request/response DTOs for the Maxio Advanced Billing API.
// Every shape below mirrors the maxio-spec/openapi.yaml contract
// (components/schemas/Customer, Product, Subscription and the matching
// Create-* request schemas); JSON is mapped with a snake_case policy.

public sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

public sealed class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

public sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

public sealed class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

/// <summary>
/// Mirrors the spec's Create-Subscription schema. Only the fields needed for
/// a payment-method-optional signup are modeled: the plan (by stable handle),
/// an existing Maxio customer, the app-supplied unique reference that makes
/// signup idempotent, and the collection method (remittance = invoice billing,
/// required since no payment profile is captured at signup).
/// </summary>
public sealed class MaxioCreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
