using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

// Models for the spec's Create-Customer-Request / Create-Customer /
// Customer-Response / Customer schemas.

public class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public required MaxioCreateCustomer Customer { get; set; }
}

public class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")]
    public required string FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public required string LastName { get; set; }

    [JsonPropertyName("email")]
    public required string Email { get; set; }

    /// <summary>Unique identifier from our app (the eShopOnWeb user id).</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

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

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}
