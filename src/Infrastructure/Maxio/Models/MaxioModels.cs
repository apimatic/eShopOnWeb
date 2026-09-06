using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Wire models for the Maxio Billing API. Property names map to the API's snake_case via the
// serializer's naming policy (see MaxioJson), so only genuinely irregular names need attributes.
// Every field is nullable-tolerant: the API adds fields over time and a response must never fail to
// deserialize because eShopOnWeb has not caught up with the catalog yet.

public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomer
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomerRequest(MaxioCustomerAttributes customer)
    {
        Customer = customer;
    }

    public MaxioCustomerAttributes Customer { get; }
}

public class MaxioCustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioProduct
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? InitialChargeInCents { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
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
    public string? Currency { get; set; }
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? TrialStartedAt { get; set; }
    public DateTimeOffset? TrialEndedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscriptionRequest(MaxioSubscriptionAttributes subscription, string uniquenessToken)
    {
        Subscription = subscription;
        UniquenessToken = uniquenessToken;
    }

    public MaxioSubscriptionAttributes Subscription { get; }

    /// <summary>
    /// Guards against duplicate submissions: Maxio rejects a repeat of the same token inside 60
    /// minutes with 409 Conflict instead of creating a second subscription.
    /// </summary>
    public string UniquenessToken { get; }
}

public class MaxioSubscriptionAttributes
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
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
    public string? Currency { get; set; }
    public bool RelationshipInvoicingEnabled { get; set; }
    public string? DefaultPaymentCollectionMethod { get; set; }
}

/// <summary>
/// Maxio returns errors either as an array of strings or as an object keyed by field. Both shapes
/// are handled when reading a failed response (see MaxioErrorReader).
/// </summary>
public class MaxioErrorResponse
{
    public IReadOnlyList<string>? Errors { get; set; }
}
