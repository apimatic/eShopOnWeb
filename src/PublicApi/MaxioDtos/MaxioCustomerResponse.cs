using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.MaxioDtos;

public class MaxioCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCustomerRequestData Customer { get; set; } = new();
}

public class MaxioCustomerRequestData
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

public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}
