using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// JSON models shaped exactly after the Maxio Advanced Billing OpenAPI specification
// (maxio-spec/openapi.yaml): responses are wrapped ({ "product": ..., "customer": ...,
// "subscription": ... }) and properties are snake_case.

public sealed class MaxioProductResponse
{
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
}

public sealed class MaxioCustomerResponse
{
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Body for POST /customers.json (Create-Customer schema in the spec).</summary>
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
}

public sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public string? Currency { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
}

/// <summary>Body for POST /subscriptions.json (Create-Subscription-Request schema in the spec).</summary>
public sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

public sealed class MaxioCreateSubscription
{
    /// <summary>The API handle of the product to subscribe to.</summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>The id of an existing Maxio customer.</summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// Payment collection method (Collection-Method schema in the spec). This API does not
    /// collect payment instruments, so subscriptions are created on remittance terms and
    /// Maxio invoices the customer instead of attempting a card charge at signup.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>The app-provided reference for the subscription (used for idempotency).</summary>
    public string? Reference { get; set; }
}
