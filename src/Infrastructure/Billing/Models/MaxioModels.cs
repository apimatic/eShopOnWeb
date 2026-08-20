using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Models;

/// <summary>OpenAPI: Product-Response</summary>
public class ProductResponse
{
    [JsonPropertyName("product")]
    public ProductDto? Product { get; set; }
}

/// <summary>OpenAPI: Product</summary>
public class ProductDto
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

    [JsonPropertyName("archived_at")]
    public string? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public ProductFamilyDto? ProductFamily { get; set; }
}

/// <summary>OpenAPI: Product-Family (nested on Product)</summary>
public class ProductFamilyDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

/// <summary>OpenAPI: Customer-Response</summary>
public class CustomerResponse
{
    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }
}

/// <summary>OpenAPI: Customer / Create-Customer</summary>
public class CustomerDto
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
}

/// <summary>OpenAPI: Create-Customer-Request</summary>
public class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomerDto Customer { get; set; } = new();
}

public class CreateCustomerDto
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

/// <summary>OpenAPI: Subscription-Response</summary>
public class SubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public SubscriptionDto? Subscription { get; set; }
}

/// <summary>OpenAPI: Subscription</summary>
public class SubscriptionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public string? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public string? NextAssessmentAt { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("product")]
    public ProductDto? Product { get; set; }

    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }
}

/// <summary>OpenAPI: Create-Subscription-Request</summary>
public class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionDto Subscription { get; set; } = new();
}

/// <summary>OpenAPI: Create-Subscription (fields used for enroll)</summary>
public class CreateSubscriptionDto
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public static class MaxioMoney
{
    public static decimal CentsToAmount(long cents) => cents / 100m;
}
