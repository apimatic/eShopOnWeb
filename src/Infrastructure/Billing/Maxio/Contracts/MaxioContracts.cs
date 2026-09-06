using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

// Wire-level shapes for the Maxio Advanced Billing (Billing API) resources this integration touches.
// Property names map to the documented snake_case payloads via JsonNamingPolicy.SnakeCaseLower, which
// is configured centrally in MaxioJson. Only the fields the integration actually uses are modelled;
// unknown members are ignored so provider additions never break us.

public class ProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
    public string? ProductPricePointHandle { get; set; }
    public string? ProductPricePointName { get; set; }
    public long? ProductPricePointId { get; set; }
}

public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

public class CustomerEnvelope
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
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class SubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long BalanceInCents { get; set; }
    public long TotalRevenueInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? TrialStartedAt { get; set; }
    public DateTimeOffset? TrialEndedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

public class SiteEnvelope
{
    public MaxioSite? Site { get; set; }
}

public class MaxioSite
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }
    public string? Currency { get; set; }
    public bool Test { get; set; }

    /// <summary>
    /// True on Relationship Invoicing sites. Decides which non-automatic collection method is valid:
    /// <c>remittance</c> under Relationship Invoicing, <c>invoice</c> on legacy Statements sites.
    /// </summary>
    public bool RelationshipInvoicingEnabled { get; set; }
}

/// <summary>
/// Create-customer payload. <c>uniqueness_token</c> sits alongside the resource key, as documented
/// under Duplicate Prevention, and makes the POST safe to retry.
/// </summary>
public class CreateCustomerRequest
{
    public CreateCustomerAttributes Customer { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

public class CreateCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Create-subscription payload.</summary>
public class CreateSubscriptionRequest
{
    public CreateSubscriptionAttributes Subscription { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

public class CreateSubscriptionAttributes
{
    /// <summary>Plans are addressed by handle: numeric ids are not stable across catalog re-seeds.</summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>An existing Maxio customer; the integration always ensures one before subscribing.</summary>
    public long CustomerId { get; set; }

    public string? ProductPricePointHandle { get; set; }

    /// <summary>
    /// How payment is collected: <c>automatic</c> charges a stored payment method, while
    /// <c>remittance</c>/<c>invoice</c> raise an invoice instead. Left null to accept the site default.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
