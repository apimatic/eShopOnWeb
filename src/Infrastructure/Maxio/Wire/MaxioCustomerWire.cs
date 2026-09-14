using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

internal class MaxioCustomerWire
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

internal class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomerWire? Customer { get; set; }
}

internal class CreateMaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CreateMaxioCustomerWire Customer { get; set; } = new();
}

internal class CreateMaxioCustomerWire
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}
