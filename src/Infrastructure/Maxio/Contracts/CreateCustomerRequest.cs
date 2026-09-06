using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>
/// Maxio OpenAPI schema <c>Create-Customer</c> (components/schemas/Create-Customer.yaml).
/// <c>first_name</c>, <c>last_name</c> and <c>email</c> are required by the schema.
/// </summary>
public class CreateCustomer
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier from this application. Maxio enforces uniqueness, which is what lets a
    /// repeated create be recognised as a duplicate rather than silently making a second customer.
    /// </summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }
}

/// <summary>Maxio OpenAPI schema <c>Create-Customer-Request</c> (components/schemas/Create-Customer-Request.yaml).</summary>
public class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomer Customer { get; set; } = new();
}
