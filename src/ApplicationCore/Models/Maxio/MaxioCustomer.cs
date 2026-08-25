using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

/// <summary>Customer per the Maxio OpenAPI spec (Customer schema).</summary>
public class MaxioCustomer
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
}

/// <summary>Wrapper per the spec's Customer-Response schema ({ "customer": { ... } }).</summary>
public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Request body per the spec's Create-Customer-Request schema.</summary>
public class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomerAttributes Customer { get; set; } = new();
}

public class MaxioCreateCustomerAttributes
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Unique identifier from our app (the eShopOnWeb user). Enforced unique by Maxio.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}
