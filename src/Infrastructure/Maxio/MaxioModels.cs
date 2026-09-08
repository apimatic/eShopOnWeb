using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Lightweight DTOs mirroring the Advanced Billing API JSON payloads.
/// Property names are mapped to snake_case by the JSON serializer options used in <see cref="MaxioApiClient"/>.
/// </summary>
public class MaxioSite
{
    public long? Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }
    public string? Currency { get; set; }
}

public class MaxioProductFamily
{
    public long? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

public class MaxioProduct
{
    public long? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool Taxable { get; set; }
    public bool RequireCreditCard { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioCustomer
{
    public long? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class MaxioSubscription
{
    public long? Id { get; set; }
    public string? State { get; set; }
    public string? Currency { get; set; }
    public long? BalanceInCents { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public string? Reference { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Wrapper matching a single "product" envelope in list responses.</summary>
public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>Wrapper matching a single "customer" envelope.</summary>
public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Wrapper matching a single "subscription" envelope in list responses.</summary>
public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Wrapper matching the "site" envelope returned by GET /site.json.</summary>
public class MaxioSiteEnvelope
{
    public MaxioSite? Site { get; set; }
}

/// <summary>Error payload shape returned by the Maxio API: {"errors": ["..."]}</summary>
public class MaxioErrorsEnvelope
{
    public List<string>? Errors { get; set; }
}
