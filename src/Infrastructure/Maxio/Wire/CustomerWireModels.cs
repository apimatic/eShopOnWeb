using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

// Wire-format models mirroring maxio-spec/components/schemas/Customer.yaml,
// Customer-Response.yaml and Create-Customer(-Request).yaml.

internal sealed class CustomerWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

internal sealed class CustomerResponseWire
{
    [JsonPropertyName("customer")]
    public CustomerWire? Customer { get; set; }
}

internal sealed class CreateCustomerWire
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

internal sealed class CreateCustomerRequestWire
{
    [JsonPropertyName("customer")]
    public CreateCustomerWire Customer { get; set; } = new();
}
