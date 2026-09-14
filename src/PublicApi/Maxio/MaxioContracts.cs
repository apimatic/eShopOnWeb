using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Wire types for the Maxio Advanced Billing REST API (https://{subdomain}.chargify.com).
/// Property names are mapped to the API's snake_case JSON via a shared serializer options object.
/// Only the subset of fields this integration needs is modeled.
/// </summary>
public static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public class MaxioSiteEnvelope
{
    public MaxioSite Site { get; set; } = new();
}

public class MaxioSite
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }
    public string? Currency { get; set; }
    public bool RelationshipInvoicingEnabled { get; set; }
    public bool Test { get; set; }
}

public class MaxioCustomerEnvelope
{
    public MaxioCustomer Customer { get; set; } = new();
}

public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

public class MaxioCustomerItem
{
    public MaxioCustomer Customer { get; set; } = new();
}

public class CreateCustomerEnvelope
{
    public MaxioCustomerDraft Customer { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

public class MaxioCustomerDraft
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
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
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductItem
{
    public MaxioProduct Product { get; set; } = new();
}

public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long BalanceInCents { get; set; }
    public long TotalRevenueInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? Currency { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioSubscriptionItem
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public class CreateSubscriptionEnvelope
{
    public MaxioSubscriptionDraft Subscription { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

public class MaxioSubscriptionDraft
{
    public string? ProductHandle { get; set; }
    public string? CustomerReference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
