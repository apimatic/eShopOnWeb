using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio.Dto;

// Wire-format DTOs for the Maxio Advanced Billing API (see maxio-spec/openapi.yaml).
// Only the fields eShopOnWeb consumes are modeled; unknown fields are ignored by System.Text.Json by default.

internal class CustomerEnvelopeDto
{
    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }
}

internal class CustomerDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

internal class CreateCustomerEnvelopeDto
{
    [JsonPropertyName("customer")]
    public required CreateCustomerDto Customer { get; set; }
}

internal class CreateCustomerDto
{
    [JsonPropertyName("first_name")]
    public required string FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public required string LastName { get; set; }

    [JsonPropertyName("email")]
    public required string Email { get; set; }

    [JsonPropertyName("reference")]
    public required string Reference { get; set; }
}
