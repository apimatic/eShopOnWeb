using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire models for the Maxio Advanced Billing API. The API is snake_case JSON with
// wrapper objects for single resources ("customer", "product", "subscription") and
// arrays of wrapped objects for list endpoints.

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Organization { get; set; }
}

public class MaxioProductFamilyRef
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool Taxable { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamilyRef? ProductFamily { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int ProductPriceInCents { get; set; }
    public int BalanceInCents { get; set; }
    public string? Currency { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

internal class MaxioCustomerWrapper
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

internal class MaxioProductWrapper
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

internal class MaxioSubscriptionWrapper
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

internal class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomerBody Customer { get; set; } = new();
}

internal class MaxioCreateCustomerBody
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string Reference { get; set; } = string.Empty;
}

internal class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscriptionBody Subscription { get; set; } = new();
}

internal class MaxioCreateSubscriptionBody
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;
}

internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Serializer = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Serializer)
            ?? throw new InvalidOperationException($"Unexpected empty response from Maxio API when deserializing {typeof(T).Name}.");
    }
}