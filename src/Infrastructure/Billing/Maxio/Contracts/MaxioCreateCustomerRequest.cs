using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>Wire shape of the <c>Create-Customer-Request</c> schema.</summary>
public sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new();
}

/// <summary>
/// Wire shape of the <c>Create-Customer</c> schema. <c>first_name</c>, <c>last_name</c> and
/// <c>email</c> are the required properties; everything else is omitted when null so we never
/// send empty strings the site would store verbatim.
/// </summary>
public sealed class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reference { get; set; }

    [JsonPropertyName("organization")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Organization { get; set; }
}
