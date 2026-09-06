using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire shapes for the Maxio Billing API calls this integration makes. Property names are
// mapped to the API's snake_case by the serializer options in MaxioApiClient, so these stay
// free of per-property attributes. Only the fields the application actually uses are declared;
// unknown fields in a response are ignored, which keeps the client working as Maxio adds more.

/// <summary>Most Maxio resources are returned wrapped in a single-property envelope.</summary>
public class MaxioSiteEnvelope
{
    public MaxioSite? Site { get; set; }
}

public class MaxioSite
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }

    /// <summary>ISO code of the site's primary currency.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Which invoicing architecture the site runs. It decides the set of payment collection
    /// methods the site accepts, so the client has to ask rather than assume.
    /// </summary>
    public bool RelationshipInvoicingEnabled { get; set; }

    public string? DefaultPaymentCollectionMethod { get; set; }

    public bool Test { get; set; }
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
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public string? ProductPricePointHandle { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

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

    /// <summary>Unique per site. This integration stores the eShopOnWeb user key here.</summary>
    public string? Reference { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>Request body for <c>POST /customers.json</c>.</summary>
public class MaxioCreateCustomerRequest
{
    public required MaxioCustomerAttributes Customer { get; set; }
}

public class MaxioCustomerAttributes
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
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

    /// <summary>When Maxio will next attempt to bill the subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Request body for <c>POST /subscriptions.json</c>.</summary>
public class MaxioCreateSubscriptionRequest
{
    public required MaxioSubscriptionAttributes Subscription { get; set; }

    /// <summary>
    /// Maxio's duplicate prevention token. A second request carrying the same token within an
    /// hour is rejected with 409 instead of being processed again. Sits alongside the
    /// <c>subscription</c> object, not inside it.
    /// </summary>
    public string? UniquenessToken { get; set; }
}

public class MaxioSubscriptionAttributes
{
    public required string ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
