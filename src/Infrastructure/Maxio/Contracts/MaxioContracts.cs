using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

// Wire contracts for the subset of the Maxio Advanced Billing API that eShopOnWeb uses. Property
// names and nullability follow the schemas in maxio-spec/components/schemas; the JSON payloads use
// snake_case, which is applied globally by MaxioSerialization rather than per-property attributes.
// Only fields eShopOnWeb reads are declared - Maxio may return more, and unknown members are ignored.

/// <summary>Envelope from <c>Customer-Response.yaml</c>.</summary>
public sealed class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Subset of <c>Customer.yaml</c>.</summary>
public sealed class MaxioCustomer
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

/// <summary>Envelope from <c>Create-Customer-Request.yaml</c>.</summary>
public sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

/// <summary>Subset of <c>Create-Customer.yaml</c>. First name, last name and e-mail are required.</summary>
public sealed class MaxioCreateCustomer
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Envelope from <c>Product-Response.yaml</c>.</summary>
public sealed class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>Subset of <c>Product.yaml</c>.</summary>
public sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? InitialChargeInCents { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>Subset of <c>Product-Family.yaml</c>.</summary>
public sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
}

/// <summary>Envelope from <c>Subscription-Response.yaml</c>.</summary>
public sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Subset of <c>Subscription.yaml</c>.</summary>
public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long BalanceInCents { get; set; }
    public long TotalRevenueInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? Currency { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? TrialStartedAt { get; set; }
    public DateTimeOffset? TrialEndedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

/// <summary>Envelope from <c>Create-Subscription-Request.yaml</c>.</summary>
public sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

/// <summary>Subset of <c>Create-Subscription.yaml</c>.</summary>
public sealed class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>Envelope from <c>Site-Response.yaml</c>.</summary>
public sealed class MaxioSiteResponse
{
    public MaxioSite? Site { get; set; }
}

/// <summary>Subset of <c>Site.yaml</c>.</summary>
public sealed class MaxioSite
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }
    public string? Currency { get; set; }
    public bool Test { get; set; }
    public bool RelationshipInvoicingEnabled { get; set; }
    public string? DefaultPaymentCollectionMethod { get; set; }
    public List<string>? NonPrimaryCurrencies { get; set; }
}
