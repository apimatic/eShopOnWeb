using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

// Data-transfer objects mirroring the Maxio Advanced Billing OpenAPI schemas (maxio-spec/).
// Property names map to the spec's snake_case fields via a snake_case JSON naming policy
// (see MaxioApiClient.JsonOptions). Only the fields eShopOnWeb consumes are modeled; the spec
// remains the authoritative contract for the full shapes.

/// <summary>Wrapper schema: Product-Response (`{ "product": { ... } }`).</summary>
internal sealed class ProductResponse
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>Subset of the spec's Product schema.</summary>
internal sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

/// <summary>Wrapper schema: Customer-Response (`{ "customer": { ... } }`).</summary>
internal sealed class CustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Subset of the spec's Customer schema.</summary>
internal sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Wrapper schema: Subscription-Response (`{ "subscription": { ... } }`).</summary>
internal sealed class SubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Subset of the spec's Subscription schema.</summary>
internal sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Reference { get; set; }
    public string? Currency { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public MaxioProduct? Product { get; set; }
}

/// <summary>Request wrapper schema: Create-Customer-Request (`{ "customer": { ... } }`).</summary>
internal sealed class CreateCustomerRequest
{
    public CreateCustomerBody Customer { get; set; } = new();
}

/// <summary>Subset of the spec's Customer-Attributes used to create a customer.</summary>
internal sealed class CreateCustomerBody
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Request wrapper schema: Create-Subscription-Request (`{ "subscription": { ... } }`).</summary>
internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionBody Subscription { get; set; } = new();
}

/// <summary>Subset of the spec's Create-Subscription used by eShopOnWeb.</summary>
internal sealed class CreateSubscriptionBody
{
    public string? ProductHandle { get; set; }
    public long CustomerId { get; set; }

    /// <summary>
    /// Invoice-based collection. Both eShop plans have no payment method on file, and an automatic
    /// (card) collection would fail to settle the balance at signup; remittance lets the subscription
    /// activate against an invoice. See Collection-Method in the spec.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    public string? Reference { get; set; }
}
