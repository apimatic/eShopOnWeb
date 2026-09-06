using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int? PriceInCents { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

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
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTime? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

public class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomerData? Customer { get; set; }
}

public class CreateCustomerData
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class CreateCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionData? Subscription { get; set; }
}

public class CreateSubscriptionData
{
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public int? ProductId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("payment_profile_id")]
    public int? PaymentProfileId { get; set; }

    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }
}

public class CreateSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public class ListProductsResponse
{
    [JsonPropertyName("products")]
    public List<MaxioProduct>? Products { get; set; }
}

public class ListSubscriptionsResponse
{
    [JsonPropertyName("subscriptions")]
    public List<MaxioSubscription>? Subscriptions { get; set; }
}

public class FindCustomerRequest
{
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class FindCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public string? Errors { get; set; }
}
