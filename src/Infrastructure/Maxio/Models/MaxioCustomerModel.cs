using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

public class MaxioCustomerModel
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

public class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomerModel Customer { get; set; } = new();
}

public class MaxioCreateCustomerRequestBody
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomerAttributes Customer { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

public class MaxioCreateCustomerAttributes
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}
