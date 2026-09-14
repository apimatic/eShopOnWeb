using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Data-transfer objects that mirror the Maxio Advanced Billing JSON shapes documented in the
// maxio-docs MCP server. All properties are (de)serialized with a snake_case naming policy configured
// centrally in MaxioServiceCollectionExtensions, so C# PascalCase maps to Maxio's snake_case keys.

// ---------------------------------------------------------------------------------------------------
// Customers
// ---------------------------------------------------------------------------------------------------

/// <summary>Maxio customer resource (subset of fields consumed by eShopOnWeb).</summary>
public class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Envelope for single-customer responses: <c>{ "customer": { ... } }</c>.</summary>
public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Attributes used to create a customer.</summary>
public class MaxioCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

/// <summary>Request body for POST /customers.json.</summary>
public class MaxioCreateCustomerRequest
{
    public MaxioCustomerAttributes Customer { get; set; } = new();
}

// ---------------------------------------------------------------------------------------------------
// Products (plans)
// ---------------------------------------------------------------------------------------------------

public class MaxioProductFamilyRef
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

/// <summary>Maxio product resource, surfaced to eShopOnWeb as a subscribable plan.</summary>
public class MaxioProduct
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
    public string? ProductPricePointHandle { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamilyRef? ProductFamily { get; set; }
}

/// <summary>Envelope for product list entries: <c>[ { "product": { ... } }, ... ]</c>.</summary>
public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

// ---------------------------------------------------------------------------------------------------
// Subscriptions
// ---------------------------------------------------------------------------------------------------

/// <summary>Maxio subscription resource (subset of fields consumed by eShopOnWeb).</summary>
public class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long TotalRevenueInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Envelope for single-subscription responses: <c>{ "subscription": { ... } }</c>.</summary>
public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Attributes used to create a subscription.</summary>
public class MaxioSubscriptionAttributes
{
    public string ProductHandle { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }

    /// <summary>
    /// How payment is collected. "remittance" enrolls the customer via invoice billing (no automatic
    /// card charge), which is what the card-less eShopOnWeb plans require.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>
/// Request body for POST /subscriptions.json. <see cref="UniquenessToken"/> is a sibling of the
/// <c>subscription</c> object (per Maxio's duplicate-prevention docs) and guards against duplicate
/// creation when a single request is retried.
/// </summary>
public class MaxioCreateSubscriptionRequest
{
    public MaxioSubscriptionAttributes Subscription { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

// ---------------------------------------------------------------------------------------------------
// Errors
// ---------------------------------------------------------------------------------------------------

/// <summary>Maxio error envelope: <c>{ "errors": [ ... ] }</c>.</summary>
public class MaxioErrorEnvelope
{
    public List<string>? Errors { get; set; }
}
