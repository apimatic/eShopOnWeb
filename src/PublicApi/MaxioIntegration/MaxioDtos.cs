using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public class MaxioProductFamilyDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class MaxioProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("product_family")]
    public MaxioProductFamilyDto? ProductFamily { get; set; }
}

public class MaxioCustomerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class MaxioSubscriptionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product_price_in_cents")]
    public int ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProductDto? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomerDto? Customer { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [JsonPropertyName("customer_attributes")]
    public MaxioCustomerAttributes? CustomerAttributes { get; set; }
}

public class MaxioCustomerAttributes
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

// Wrapper types for Maxio API responses (which nest objects under type-named keys)
public class ProductListResponse
{
    [JsonPropertyName("items")]
    public List<ProductListItem> Items { get; set; } = new();
}

public class ProductListItem
{
    [JsonPropertyName("product")]
    public MaxioProductDto Product { get; set; } = new();
}

public class CustomerListResponse
{
    [JsonPropertyName("items")]
    public List<CustomerListItem> Items { get; set; } = new();
}

public class CustomerListItem
{
    [JsonPropertyName("customer")]
    public MaxioCustomerDto Customer { get; set; } = new();
}

public class SubscriptionListResponse
{
    [JsonPropertyName("items")]
    public List<SubscriptionListItem> Items { get; set; } = new();
}

public class SubscriptionListItem
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionDto Subscription { get; set; } = new();
}

public class SingleCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomerDto Customer { get; set; } = new();
}

public class SingleSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionDto Subscription { get; set; } = new();
}
