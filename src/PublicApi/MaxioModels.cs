using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi;

// Customer models
public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

// Product models
public class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }
}

public class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

public class MaxioProductListResponse
{
    [JsonPropertyName("items")]
    public List<MaxioProductResponse>? Items { get; set; }
}

// Subscription models
public class MaxioSubscriptionProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public int? ProductId { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("next_billing_at")]
    public DateTime? NextBillingAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioSubscriptionProduct? Product { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }
}

public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscriptionListResponse
{
    [JsonPropertyName("subscriptions")]
    public List<MaxioSubscription>? Subscriptions { get; set; }
}

// Create subscription request
public class CreateMaxioSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionData? Subscription { get; set; }
}

public class CreateSubscriptionData
{
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_attributes")]
    public CreateCustomerData? CustomerAttributes { get; set; }
}

public class CreateCustomerData
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}
