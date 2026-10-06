using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Request/response models mirroring the Maxio Advanced Billing OpenAPI spec
// (maxio-spec/openapi.yaml). Serialized with snake_case naming policy.
// List responses are JSON arrays of envelope objects, e.g. [{"product": {...}}].

/// <summary>POST /customers.json request payload (Create-Customer schema).</summary>
public class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    /// <summary>The app's own unique customer identifier.</summary>
    public string? Reference { get; set; }
}

/// <summary>Customer object (Customer schema).</summary>
public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Product family object (Product-Family schema).</summary>
public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

/// <summary>Product object (Product schema) — a subscription plan.</summary>
public class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
    public string? ProductPricePointName { get; set; }
}

/// <summary>Subscription object (Subscription schema).</summary>
public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

/// <summary>POST /subscriptions.json request payload (Create-Subscription schema).</summary>
public class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    /// <summary>The app's own unique reference for the subscription.</summary>
    public string? Reference { get; set; }
    /// <summary>
    /// Collection-Method enum value. The demo catalog's plans do not require a
    /// payment method, so enrollments are created on remittance (billed-invoice)
    /// terms instead of attempting an automatic signup charge.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}

// Response envelopes

public class CustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

public class SubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

public class ProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public class ProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

// Request envelopes (the spec wraps create payloads, e.g. {"customer": {...}})

public class CreateCustomerEnvelope
{
    public MaxioCreateCustomer? Customer { get; set; }
}

public class CreateSubscriptionEnvelope
{
    public MaxioCreateSubscription? Subscription { get; set; }
}