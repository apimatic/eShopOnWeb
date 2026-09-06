using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Wire model for the specification's <c>Customer</c> schema (only the members this integration reads).</summary>
public sealed record MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("organization")]
    public string? Organization { get; init; }

    /// <summary>The unique identifier used within the calling application for this customer.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>Wire model for the specification's <c>Customer Response</c> schema.</summary>
public sealed record CustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; init; }
}

/// <summary>Wire model for the specification's <c>Create Customer</c> schema.</summary>
public sealed record CreateCustomer
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

/// <summary>Wire model for the specification's <c>Create Customer Request</c> schema.</summary>
public sealed record CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public required CreateCustomer Customer { get; init; }
}
