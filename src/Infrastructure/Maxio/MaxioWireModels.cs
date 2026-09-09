using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire models for the Maxio Billing API. Property names are serialized with a
// snake_case naming policy by MaxioClient, so ProductId maps to "product_id", etc.

internal class MaxioProductWire
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

internal class MaxioCustomerWire
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
}

internal class MaxioSubscriptionWire
{
    public int Id { get; set; }
    public string? State { get; set; }
    public int? CustomerId { get; set; }
    public int? ProductId { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public MaxioProductWire? Product { get; set; }
    public MaxioCustomerWire? Customer { get; set; }
}

internal class MaxioProductResponseWire
{
    public MaxioProductWire? Product { get; set; }
}

internal class MaxioCustomerResponseWire
{
    public MaxioCustomerWire? Customer { get; set; }
}

internal class MaxioSubscriptionResponseWire
{
    public MaxioSubscriptionWire? Subscription { get; set; }
}

internal class MaxioCreateSubscriptionRequestWire
{
    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }

    public MaxioCreateSubscriptionBodyWire? Subscription { get; set; }
}

internal class MaxioCreateSubscriptionBodyWire
{
    public string? ProductHandle { get; set; }
    public int CustomerId { get; set; }

    /// <summary>
    /// The seeded plans do not require a card at signup; remittance billing creates the
    /// subscription without an immediate charge attempt (invoice-based collection).
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal class MaxioCreateCustomerRequestWire
{
    public MaxioCreateCustomerBodyWire? Customer { get; set; }
}

internal class MaxioCreateCustomerBodyWire
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string? Organization { get; set; }
}
