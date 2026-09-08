using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Contracts;

/// <summary>
/// Wire models for the Maxio Advanced Billing API.
///
/// These mirror the schemas declared in the Maxio OpenAPI specification
/// (maxio-spec/) — the authoritative contract for every Maxio interaction.
/// Property names are serialized/deserialized using a snake_case naming policy.
/// </summary>

// ---------------------------------------------------------------------
// Customers
// ---------------------------------------------------------------------

public sealed class CustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
}

/// <summary>POST /customers.json request body.</summary>
public sealed class CreateCustomerRequest
{
    public CustomerAttributes Customer { get; set; } = new CustomerAttributes();
}

public sealed class Customer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
}

public sealed class CustomerResponse
{
    public Customer? Customer { get; set; }
}

// ---------------------------------------------------------------------
// Products (plans) and product families
// ---------------------------------------------------------------------

public sealed class ProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
}

public sealed class Product
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public int? DefaultProductPricePointId { get; set; }
    public int? ProductPricePointId { get; set; }
    public string? ProductPricePointName { get; set; }
    public string? ProductPricePointHandle { get; set; }
    public ProductFamily? ProductFamily { get; set; }
}

public sealed class ProductResponse
{
    public Product? Product { get; set; }
}

// ---------------------------------------------------------------------
// Subscriptions
// ---------------------------------------------------------------------

/// <summary>POST /subscriptions.json request body.</summary>
public sealed class CreateSubscription
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? Reference { get; set; }

    /// <summary>
    /// When present (future date), the subscription is created without an initial charge and no
    /// payment is captured; the first billing occurs at this time. Used to support signup on
    /// plans that do not require a stored payment method (no card / 3-DS capture at signup).
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; set; }
}

public sealed class CreateSubscriptionRequest
{
    public CreateSubscription Subscription { get; set; } = new CreateSubscription();
}

public sealed class Subscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? Currency { get; set; }
    public string? Reference { get; set; }
    public int? ProductPricePointId { get; set; }
    public Customer? Customer { get; set; }
    public Product? Product { get; set; }
}

public sealed class SubscriptionResponse
{
    public Subscription? Subscription { get; set; }
}

/// <summary>Array-shaped list responses are envelopes, one object per array element.</summary>
public sealed class CustomerListResponseItem
{
    public Customer? Customer { get; set; }
}

public sealed class ProductListResponseItem
{
    public Product? Product { get; set; }
}

public sealed class SubscriptionListResponseItem
{
    public Subscription? Subscription { get; set; }
}
