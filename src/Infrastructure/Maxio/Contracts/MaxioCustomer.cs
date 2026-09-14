using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Envelope Maxio wraps a customer in, both on read and on create.</summary>
internal sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Body of <c>POST /customers.json</c>.</summary>
internal sealed class CreateMaxioCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateMaxioCustomer Customer { get; set; } = new();

    /// <summary>
    /// Long random value that lets Maxio reject a duplicate submission of this same request within
    /// 60 minutes with 409 Conflict rather than creating a second customer.
    /// </summary>
    [JsonPropertyName("uniqueness_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateMaxioCustomer
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("organization")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Organization { get; set; }
}
