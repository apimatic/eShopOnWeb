using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioProductListItem
{
    [JsonPropertyName("product")] public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProductResponse
{
    [JsonPropertyName("product")] public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string IntervalUnit { get; set; } = string.Empty;
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("taxable")] public bool Taxable { get; set; }
    [JsonPropertyName("product_family")] public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("handle")] public string Handle { get; set; } = string.Empty;
}

public sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")] public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioSiteResponse
{
    [JsonPropertyName("site")] public MaxioSite Site { get; set; } = new();
}

public sealed class MaxioSite
{
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
}

public sealed class MaxioCustomerRequest
{
    [JsonPropertyName("customer")] public MaxioCustomerAttributes Customer { get; set; } = new();
}

public sealed class MaxioCustomerAttributes
{
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
}

public sealed class MaxioSubscriptionRequest
{
    [JsonPropertyName("subscription")] public MaxioSubscriptionAttributes Subscription { get; set; } = new();
}

public sealed class MaxioSubscriptionAttributes
{
    [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
    [JsonPropertyName("customer_id")] public int CustomerId { get; set; }
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
    [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; set; } = string.Empty;
}

public sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")] public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")] public long ProductPriceInCents { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}
