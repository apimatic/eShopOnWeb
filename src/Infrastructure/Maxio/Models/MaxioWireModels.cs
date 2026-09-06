using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Wire models for the Maxio Advanced Billing API. Property names are mapped by the snake_case
// naming policy configured in MaxioApiClient, so only names that do not round-trip through that
// policy carry an explicit [JsonPropertyName].
//
// Shapes verified against the official Maxio Advanced Billing OpenAPI export
// (https://developers.maxio.com/static/exports/maxio-advanced-billing-swagger20.json) and against
// live responses from a Maxio sandbox site. Only the fields this integration consumes are modelled;
// unknown fields are ignored by System.Text.Json.

/// <summary>Envelope returned by <c>GET /site.json</c>.</summary>
public class MaxioSiteEnvelope
{
    public MaxioSite? Site { get; set; }
}

public class MaxioSite
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }

    /// <summary>ISO-4217 currency of the site, e.g. <c>USD</c>.</summary>
    public string? Currency { get; set; }

    public bool? RelationshipInvoicingEnabled { get; set; }
    public string? DefaultPaymentCollectionMethod { get; set; }
    public bool? Test { get; set; }
}

/// <summary>Envelope used by the product endpoints.</summary>
public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }

    /// <summary>Set when the product has been archived and can no longer be subscribed to.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>True when Maxio refuses a signup without a stored payment profile.</summary>
    public bool? RequireCreditCard { get; set; }

    public bool? Taxable { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>Envelope returned by <c>GET /product_families.json</c>.</summary>
public class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

/// <summary>Envelope used by the customer endpoints.</summary>
public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }

    /// <summary>The caller-owned unique identifier for this customer. Unique per site, case-insensitively.</summary>
    public string? Reference { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Request body for <c>POST /customers.json</c>.</summary>
public class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

public class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

/// <summary>Envelope used by the subscription endpoints.</summary>
public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public string? Currency { get; set; }
    public long? BalanceInCents { get; set; }
    public long? ProductPriceInCents { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio will next assess (bill) the subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Request body for <c>POST /subscriptions.json</c>.</summary>
public class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

public class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }

    /// <summary>
    /// Id of the existing Maxio customer to attach the subscription to.
    /// </summary>
    /// <remarks>
    /// Maxio also accepts <c>customer_reference</c> here, but that lookup does not resolve
    /// references containing a <c>+</c> (verified against a sandbox site: the call fails with
    /// "A Customer must be specified for the subscription to be valid."). Since eShopOnWeb derives
    /// customer references from email addresses, which routinely contain <c>+</c>, the numeric id
    /// returned by the find-or-create step is used instead.
    /// </remarks>
    public long? CustomerId { get; set; }

    /// <summary>Caller-owned reference for the subscription itself. Unique per site.</summary>
    public string? Reference { get; set; }

    public string? PaymentCollectionMethod { get; set; }
}
