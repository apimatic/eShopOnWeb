using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomerJson? Customer { get; set; }
}

internal sealed class MaxioCustomerJson
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomerBody Customer { get; set; } = new();
}

internal sealed class MaxioCreateCustomerBody
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProductJson? Product { get; set; }
}

internal sealed class MaxioProductJson
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

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

    [JsonPropertyName("archived_at")]
    public string? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamilyJson? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamilyJson
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

internal sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionJson? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionJson
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public string? NextAssessmentAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public string? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProductJson? Product { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscriptionBody Subscription { get; set; } = new();
}

internal sealed class MaxioCreateSubscriptionBody
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal sealed class MaxioErrorListResponse
{
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }
}
