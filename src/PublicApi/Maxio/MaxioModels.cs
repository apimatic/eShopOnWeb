using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioProduct
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("handle")] public string? Handle { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string IntervalUnit { get; set; } = string.Empty;
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("taxable")] public bool Taxable { get; set; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("activated_at")] public DateTimeOffset? ActivatedAt { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; set; }
}

public sealed class MaxioSite
{
    [JsonPropertyName("relationship_invoicing_enabled")] public bool RelationshipInvoicingEnabled { get; set; }
    [JsonPropertyName("default_payment_collection_method")] public string? DefaultPaymentCollectionMethod { get; set; }
}

public sealed class MaxioItem<T>
{
    [JsonPropertyName("product")] public T? Product { get; set; }
    [JsonPropertyName("subscription")] public T? Subscription { get; set; }
}

public sealed class MaxioItemsResponse<T>
{
    [JsonPropertyName("items")] public List<MaxioItem<T>> Items { get; set; } = new();
}

public sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioSiteResponse
{
    [JsonPropertyName("site")] public MaxioSite? Site { get; set; }
}
