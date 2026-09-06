using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = null!;

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = null!;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = null!;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = null!;
}

public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = null!;
}

public class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }
}

public class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = null!;
}

public class MaxioProductsListResponse
{
    [JsonPropertyName("products")]
    public List<MaxioProduct> Products { get; set; } = new();
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = null!;

    [JsonPropertyName("state")]
    public string State { get; set; } = null!;

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("current_price")]
    public decimal CurrentPrice { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = null!;
}

public class MaxioSubscriptionsListResponse
{
    [JsonPropertyName("subscriptions")]
    public List<MaxioSubscription> Subscriptions { get; set; } = new();
}

public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionData Subscription { get; set; } = null!;
}

public class CreateSubscriptionData
{
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = null!;
}

public class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomerData Customer { get; set; } = null!;
}

public class CreateCustomerData
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = null!;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = null!;

    [JsonPropertyName("email")]
    public string Email { get; set; } = null!;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = null!;
}
