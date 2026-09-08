using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class MaxioProduct
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public long? InitialChargeInCents { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public int? ExpirationInterval { get; set; }
    public string? ExpirationIntervalUnit { get; set; }
    public bool RequiresCreditCard { get; set; }
    public bool Taxable { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public long? DefaultProductPricePointId { get; set; }
    public long? ProductPricePointId { get; set; }
    public string? ProductPricePointHandle { get; set; }
    public string? ProductPricePointName { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long? BalanceInCents { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? TrialStartedAt { get; set; }
    public DateTimeOffset? TrialEndedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? SignupRevenue { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioSite
{
    public bool RelationshipInvoicingEnabled { get; set; }
}

public sealed class MaxioSiteEnvelope
{
    public MaxioSite? Site { get; set; }
}
