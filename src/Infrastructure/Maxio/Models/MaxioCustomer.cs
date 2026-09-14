using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Maxio Advanced Billing <c>Customer</c>.
/// Shape defined by <c>maxio-spec/components/schemas/Customer.yaml</c>.
/// </summary>
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

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    /// <summary>The unique identifier eShopOnWeb uses for this shopper.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Maxio <c>Customer Response</c> envelope.
/// </summary>
public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>
/// Body of <c>POST /customers.json</c>, per <c>Create-Customer-Request.yaml</c>.
/// </summary>
public class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new();
}

/// <summary>
/// Maxio <c>Create Customer</c> attributes. <c>first_name</c>, <c>last_name</c> and <c>email</c>
/// are required by the spec; the remaining properties are omitted when null.
/// </summary>
public class MaxioCreateCustomer
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
