using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

public sealed class ProductResponse
{
    [JsonPropertyName("product")]
    public Product? Product { get; set; }
}

public sealed class Product
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
    public ProductFamily? ProductFamily { get; set; }
}

public sealed class ProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

public sealed class CustomerResponse
{
    [JsonPropertyName("customer")]
    public Customer? Customer { get; set; }
}

public sealed class Customer
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

public sealed class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomer Customer { get; set; } = new();
}

public sealed class CreateCustomer
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

public sealed class SubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public Subscription? Subscription { get; set; }
}

public sealed class Subscription
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

    [JsonPropertyName("customer")]
    public Customer? Customer { get; set; }

    [JsonPropertyName("product")]
    public Product? Product { get; set; }
}

public sealed class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscription Subscription { get; set; } = new();
}

public sealed class CreateSubscription
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class ErrorListResponse
{
    [JsonPropertyName("errors")]
    public object? Errors { get; set; }
}

internal static class ErrorListParser
{
    public static IReadOnlyList<string> Parse(object? errors)
    {
        if (errors is null)
        {
            return Array.Empty<string>();
        }

        if (errors is System.Text.Json.JsonElement element)
        {
            return ParseElement(element);
        }

            return new[] { errors.ToString() ?? "Unknown billing error." };
    }

    private static IReadOnlyList<string> ParseElement(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    list.Add(item.GetString() ?? string.Empty);
                }
                else
                {
                    list.Add(item.ToString());
                }
            }
            return list;
        }

        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var list = new List<string>();
            foreach (var property in element.EnumerateObject())
            {
                list.Add($"{property.Name}: {property.Value}");
            }
            return list;
        }

        if (element.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return new[] { element.GetString() ?? string.Empty };
        }

        return new[] { element.ToString() };
    }
}
