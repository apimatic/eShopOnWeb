using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Internal wire contracts for the Maxio (Advanced Billing / Chargify-lineage) REST API.
// Property names are mapped to the API's snake_case via a JsonNamingPolicy configured on the
// serializer (see MaxioSubscriptionService), so C# PascalCase names map to snake_case JSON
// (e.g. PriceInCents <-> price_in_cents). Envelope wrappers mirror the API's habit of nesting
// each resource under a singular key (e.g. {"product": {...}}).

internal sealed class ProductFamilyEnvelope
{
    public ProductFamilyResource? ProductFamily { get; set; }
}

internal sealed class ProductFamilyResource
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class ProductEnvelope
{
    public ProductResource? Product { get; set; }
}

internal sealed class ProductResource
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public ProductFamilyResource? ProductFamily { get; set; }
}

internal sealed class CustomerEnvelope
{
    public CustomerResource? Customer { get; set; }
}

internal sealed class CustomerResource
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    public SubscriptionResource? Subscription { get; set; }
}

internal sealed class SubscriptionResource
{
    public long Id { get; set; }
    public string? State { get; set; }
    public int ProductPriceInCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public ProductResource? Product { get; set; }
    public CustomerResource? Customer { get; set; }
}

// ---- Request bodies ----

internal sealed class CreateCustomerRequest
{
    public CustomerAttributes Customer { get; set; } = new();
}

internal sealed class CustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public SubscriptionAttributes Subscription { get; set; } = new();
}

internal sealed class SubscriptionAttributes
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
