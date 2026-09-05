using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure;

public class MaxioCustomerRequest
{
    [JsonPropertyName("customer")]
    public CustomerData Customer { get; set; } = new();

    public class CustomerData
    {
        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("organization")]
        public string? Organization { get; set; }
    }
}

public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public Customer? Customer { get; set; }
}

public class Customer
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

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

public class MaxioSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public SubscriptionData Subscription { get; set; } = new();

    public class SubscriptionData
    {
        [JsonPropertyName("product_handle")]
        public string ProductHandle { get; set; } = string.Empty;

        [JsonPropertyName("customer_id")]
        public int? CustomerId { get; set; }

        [JsonPropertyName("customer_reference")]
        public string? CustomerReference { get; set; }

        [JsonPropertyName("payment_collection_method")]
        public string PaymentCollectionMethod { get; set; } = "automatic";
    }
}

public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public Subscription? Subscription { get; set; }
}

public class Subscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("customer")]
    public Customer? Customer { get; set; }

    [JsonPropertyName("product")]
    public Product? Product { get; set; }

    [JsonPropertyName("current_period_starts_at")]
    public DateTime? CurrentPeriodStartsAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }
}

public class Product
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }
}

public class MaxioSubscriptionsListResponse
{
    [JsonPropertyName("subscriptions")]
    public List<Subscription> Subscriptions { get; set; } = new();
}

public class MaxioCustomersListResponse
{
    [JsonPropertyName("customers")]
    public List<Customer> Customers { get; set; } = new();
}
