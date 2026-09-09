using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

// Data-transfer types mirroring the Maxio Advanced Billing OpenAPI contract (maxio-spec/).
// Property names are PascalCase and serialized/deserialized as snake_case via a JSON naming policy,
// matching the spec's field names (e.g. PriceInCents <-> price_in_cents). Only the fields used by the
// subscription-billing flow are modelled; the JSON serializer ignores the rest.

// ---- Products (plans) ----

/// <summary>Wrapper matching Product-Response.yaml: { "product": { ... } }.</summary>
public sealed class ProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>Subset of Product.yaml.</summary>
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
    public bool Taxable { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>Subset of Product-Family.yaml (nested in a product).</summary>
public sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

// ---- Customers ----

/// <summary>Wrapper matching Customer-Response.yaml: { "customer": { ... } }.</summary>
public sealed class CustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Subset of Customer.yaml.</summary>
public sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Wrapper matching Create-Customer-Request.yaml: { "customer": { ... } }.</summary>
public sealed class CreateCustomerRequest
{
    public CreateCustomerBody Customer { get; set; } = new();
}

/// <summary>Subset of Create-Customer.yaml (first_name, last_name, email are required by the spec).</summary>
public sealed class CreateCustomerBody
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

// ---- Subscriptions ----

/// <summary>Wrapper matching Subscription-Response.yaml: { "subscription": { ... } }.</summary>
public sealed class SubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Subset of Subscription.yaml.</summary>
public sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Wrapper matching Create-Subscription-Request.yaml: { "subscription": { ... } }.</summary>
public sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionBody Subscription { get; set; } = new();
}

/// <summary>
/// Subset of Create-Subscription.yaml. We identify an existing customer by id and select the plan by its
/// stable product handle.
/// </summary>
public sealed class CreateSubscriptionBody
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }

    /// <summary>
    /// Payment collection method (Collection-Method.yaml). Set to "remittance" so a subscription can be
    /// created without a stored payment method — no charge/3-DS is attempted at signup, matching plans
    /// configured with "payment method not required".
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
