using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

/// <summary>
/// Minimal, read/write-shaped DTOs for the Maxio Advanced Billing (Chargify) JSON API.
/// Field names use Maxio's snake_case wire format. Only the fields this integration
/// consumes are mapped; unknown fields are ignored on deserialize.
/// </summary>
public sealed class MaxioCustomer
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

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}

public sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioSubscriptionProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public int? ProductPriceInCents { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public int? BalanceInCents { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTime? CanceledAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioSubscriptionProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>
/// Thrown when Maxio Advanced Billing returns a non-success HTTP response.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(string requestDescription, int statusCode, string responseBody)
        : base($"Maxio API call '{requestDescription}' failed with HTTP {(int)statusCode}: {responseBody}")
    {
        RequestDescription = requestDescription;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public string RequestDescription { get; }
    public int StatusCode { get; }
    public string ResponseBody { get; }
}