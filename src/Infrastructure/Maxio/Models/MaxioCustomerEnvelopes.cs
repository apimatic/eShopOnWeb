using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Maxio <c>Customer Response</c> (<c>maxio-spec/components/schemas/Customer-Response.yaml</c>).
/// </summary>
public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>
/// Maxio <c>Create Customer Request</c>
/// (<c>maxio-spec/components/schemas/Create-Customer-Request.yaml</c>).
/// </summary>
public class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new();
}

/// <summary>
/// Maxio <c>Create Customer</c> (<c>maxio-spec/components/schemas/Create-Customer.yaml</c>).
/// </summary>
/// <remarks>
/// The specification marks <c>first_name</c>, <c>last_name</c> and <c>email</c> as required; the
/// rest of the schema is optional and only the properties eShopOnWeb populates are transcribed.
/// </remarks>
public class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}
