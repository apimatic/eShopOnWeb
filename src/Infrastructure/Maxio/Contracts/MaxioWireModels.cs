using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

// Wire models for the Maxio Advanced Billing REST API.
//
// Property names are PascalCase counterparts of Maxio's snake_case JSON; the serializer options in
// MaxioJson apply JsonNamingPolicy.SnakeCaseLower, so no per-property attributes are needed.
// Only the fields this integration actually consumes are modelled — Maxio's payloads are large and
// binding the rest would be dead weight that still has to be kept correct.
//
// Every shape below was confirmed against live responses from a Maxio sandbox site, not inferred.

/// <summary>Maxio wraps single resources in a one-property envelope, e.g. <c>{"customer": {...}}</c>.</summary>
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
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

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
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }

    /// <summary>True when Maxio requires a stored payment method before this product can be sold.</summary>
    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? Currency { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio will next assess (bill) the subscription — the next billing date.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? TrialStartedAt { get; set; }
    public DateTimeOffset? TrialEndedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioSiteEnvelope
{
    public MaxioSite? Site { get; set; }
}

public class MaxioSite
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }

    /// <summary>ISO code of the site's primary currency, e.g. <c>USD</c>.</summary>
    public string? Currency { get; set; }

    public string? DefaultPaymentCollectionMethod { get; set; }
}

public class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomerRequest(MaxioCustomerAttributes customer) => Customer = customer;

    public MaxioCustomerAttributes Customer { get; }
}

public class MaxioCustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscriptionRequest(MaxioSubscriptionAttributes subscription) => Subscription = subscription;

    public MaxioSubscriptionAttributes Subscription { get; }
}

public class MaxioSubscriptionAttributes
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? Reference { get; set; }
}

/// <summary>
/// Maxio's error envelope. The <c>errors</c> member is a JSON array of strings on validation
/// failures; it is modelled loosely because other failure modes return a bare message instead.
/// </summary>
public class MaxioErrorEnvelope
{
    public List<string>? Errors { get; set; }
    public string? Error { get; set; }
}
