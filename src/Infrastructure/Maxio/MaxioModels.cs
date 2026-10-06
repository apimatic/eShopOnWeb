using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTime? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public long ProductPriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentCollectionMethod { get; set; } = string.Empty;
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

public class MaxioSite
{
    public string Currency { get; set; } = string.Empty;
}

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class SiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite? Site { get; set; }
}