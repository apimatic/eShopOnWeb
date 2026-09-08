using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

public sealed class MaxioSite
{
    public long Id { get; set; }
    public string Subdomain { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
}

public sealed class MaxioSiteEnvelope
{
    public MaxioSite Site { get; set; } = new();
}

public sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioProduct
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool Taxable { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class MaxioProductEnvelope
{
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; set; } = new();
}
