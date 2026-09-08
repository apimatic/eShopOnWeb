using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Maxio.Http;

// Raw DTOs that mirror the Maxio Advanced Billing JSON payloads. Property names are
// mapped from snake_case via JsonSerializerOptions in MaxioApiClient. Only the fields
// this integration uses are declared.

public sealed class MaxioSite
{
    public long? Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }
    public string? Currency { get; set; }
}

public sealed class MaxioProductFamily
{
    public long? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class MaxioProduct
{
    public long? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? InitialChargeInCents { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool RequestCreditCard { get; set; }
    public bool? Taxable { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioCustomer
{
    public long? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool? Verified { get; set; }
}

public sealed class MaxioSubscription
{
    public long? Id { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public long? BalanceInCents { get; set; }
    public long? ProductPriceInCents { get; set; }
    public string? Currency { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? CancellationMethod { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

// Payloads sent to Maxio.

public sealed class MaxioNewCustomer
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public sealed class MaxioNewSubscription
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

// Envelope wrappers. Maxio wraps single resources and array entries in an object
// named after the resource.

public sealed class MaxioSiteEnvelope
{
    public MaxioSite Site { get; set; } = new MaxioSite();
}

public sealed class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily ProductFamily { get; set; } = new MaxioProductFamily();
}

public sealed class MaxioProductEnvelope
{
    public MaxioProduct Product { get; set; } = new MaxioProduct();
}

public sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer Customer { get; set; } = new MaxioCustomer();
}

public sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; set; } = new MaxioSubscription();
}

public sealed class MaxioCreateCustomerEnvelope
{
    public MaxioNewCustomer Customer { get; set; } = new MaxioNewCustomer();
}

public sealed class MaxioCreateSubscriptionEnvelope
{
    public MaxioNewSubscription Subscription { get; set; } = new MaxioNewSubscription();
}
