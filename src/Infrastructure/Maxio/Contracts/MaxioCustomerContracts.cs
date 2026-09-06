using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>
/// maxio-spec/components/schemas/Customer-Response.yaml - the envelope every single-customer
/// operation returns.
/// </summary>
public sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>
/// maxio-spec/components/schemas/Customer.yaml. Only the fields this integration consumes are
/// declared; unknown members are ignored on deserialization.
/// </summary>
public sealed class MaxioCustomer
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

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>maxio-spec/components/schemas/Create-Customer-Request.yaml.</summary>
public sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new();
}

/// <summary>
/// maxio-spec/components/schemas/Create-Customer.yaml. first_name, last_name and email are the
/// schema's required members.
/// </summary>
public sealed class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    /// <summary>
    /// "A customer reference, or unique identifier from your app". Maxio enforces uniqueness, which
    /// is what makes customer creation idempotent for a given eShopOnWeb user.
    /// </summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}
