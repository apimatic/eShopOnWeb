using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio.Models;

/// <summary>Wrapper for a single customer, per the spec's <c>Customer-Response</c> schema.</summary>
public sealed record MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; init; }
}

/// <summary>A billing-system customer. Fields mirror the spec's <c>Customer</c> schema (subset used here).</summary>
public sealed record MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }
}

/// <summary>Request body for <c>POST /customers.json</c>, per the spec's <c>Create-Customer-Request</c>.</summary>
public sealed record CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public required CreateCustomerBody Customer { get; init; }
}

/// <summary>
/// The customer attributes to create, per the spec's <c>Create-Customer</c> schema.
/// <c>first_name</c>, <c>last_name</c> and <c>email</c> are required; <c>reference</c> is the app's stable key.
/// </summary>
public sealed record CreateCustomerBody
{
    [JsonPropertyName("first_name")]
    public required string FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public required string LastName { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }
}
