using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Wire-level shapes for the Maxio Advanced Billing REST API. Property names map to the API's
// snake_case JSON via JsonNamingPolicy.SnakeCaseLower (see MaxioJson). Only the fields this
// integration actually reads are modelled; unknown fields are ignored on deserialization.
//
// The Advanced Billing API wraps single resources in an envelope named after the resource
// ("customer", "subscription", "product", "product_family", "site") and returns collections as
// arrays of those envelopes.

internal sealed class MaxioSiteEnvelope
{
    public MaxioSite? Site { get; set; }
}

internal sealed class MaxioSite
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }

    /// <summary>Default currency of the billing site, e.g. "USD".</summary>
    public string? Currency { get; set; }

    public bool RelationshipInvoicingEnabled { get; set; }

    /// <summary>True when this is a test (sandbox) site.</summary>
    public bool Test { get; set; }
}

internal sealed class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }

    /// <summary>Recurring price in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Number of interval units in a billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Interval unit, e.g. "month" or "day".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>Whether a payment profile must be captured before a subscription can be created.</summary>
    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }

    /// <summary>Unique, caller-assigned identifier. Maxio enforces uniqueness per site.</summary>
    public string? Reference { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public long Id { get; set; }

    /// <summary>Provider lifecycle state, e.g. "active", "past_due", "canceled".</summary>
    public string? State { get; set; }

    public long BalanceInCents { get; set; }

    /// <summary>The recurring amount of the product version currently subscribed to, in cents.</summary>
    public long ProductPriceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When capture of the next payment will be attempted.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Unique, caller-assigned identifier. Maxio enforces uniqueness per site.</summary>
    public string? Reference { get; set; }

    public string? Currency { get; set; }

    public MaxioProduct? Product { get; set; }

    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

internal sealed class MaxioCreateCustomer
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

internal sealed class MaxioCreateSubscription
{
    /// <summary>API handle of the product to subscribe to.</summary>
    public string? ProductHandle { get; set; }

    /// <summary>Id of an existing customer. Preferred over customer_reference once resolved.</summary>
    public long? CustomerId { get; set; }

    /// <summary>Caller-assigned reference for the subscription; the idempotency anchor.</summary>
    public string? Reference { get; set; }

    /// <summary>Collection method, e.g. "automatic" or "remittance".</summary>
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>
/// Error payload. Advanced Billing returns either {"errors": ["..."]} or, for some endpoints,
/// {"errors": {"field": ["..."]}}; both are handled by <see cref="MaxioErrorReader"/>.
/// </summary>
internal sealed class MaxioErrorsArrayResponse
{
    public List<string>? Errors { get; set; }
}
