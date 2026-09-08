using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// The types in this file mirror the JSON shapes published in the Maxio Advanced Billing OpenAPI
// specification (see /maxio-spec). Only the attributes this integration relies on are modeled;
// unknown members returned by the API are ignored. Property names are snake_case on the wire.

/// <summary>
/// A customer (per <c>components/schemas/Customer.yaml</c>).
/// </summary>
public sealed class MaxioCustomer
{
    public long? Id { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Reference { get; set; }

    public string? Organization { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>
/// Product family (per <c>components/schemas/Product-Family.yaml</c>).
/// </summary>
public sealed class MaxioProductFamily
{
    public long? Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// A product / plan (per <c>components/schemas/Product.yaml</c>).
/// </summary>
public sealed class MaxioProduct
{
    public long? Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public long? InitialChargeInCents { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public string? ArchivedAt { get; set; }

    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }

    public string? ProductPricePointName { get; set; }

    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>
/// A subscription (per <c>components/schemas/Subscription.yaml</c>).
/// </summary>
public sealed class MaxioSubscription
{
    public long? Id { get; set; }

    public string? State { get; set; }

    public long BalanceInCents { get; set; }

    public long TotalRevenueInCents { get; set; }

    public long ProductPriceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public string? Currency { get; set; }

    public string? Reference { get; set; }

    public MaxioCustomer? Customer { get; set; }

    public MaxioProduct? Product { get; set; }
}

// ---- Response wrappers. Advanced Billing wraps single resources as "{resource}: { ... }" and
// returns bare arrays for list operations. ----

public sealed class CustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public sealed class ProductResponse
{
    public MaxioProduct? Product { get; set; }
}

public sealed class SubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

// ---- Request payloads (see components/schemas/Create-Customer-Request.yaml and
// Create-Subscription-Request.yaml). ----

public sealed class CreateCustomerRequest
{
    public CreateCustomerRequest(MaxioCustomer customer)
    {
        Customer = customer;
    }

    public MaxioCustomer Customer { get; }
}

public sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionRequest(CreateSubscriptionAttributes subscription)
    {
        Subscription = subscription;
    }

    public CreateSubscriptionAttributes Subscription { get; }
}

public sealed class CreateSubscriptionAttributes
{
    public string? ProductHandle { get; set; }

    public long? CustomerId { get; set; }

    /// <summary>
    /// When supplied, no trial/initial charge is captured at signup and the first billing
    /// assessment is scheduled for this timestamp.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; set; }
}
