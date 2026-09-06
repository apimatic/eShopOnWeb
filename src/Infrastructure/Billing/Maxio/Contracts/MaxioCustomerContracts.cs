using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>
/// Wire contracts for Maxio's customer resource. Shapes verified against
/// <c>POST /customers.json</c> and <c>GET /customers/lookup.json?reference=…</c> on a live site.
/// </summary>
public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>The caller-owned identifier that maps this customer back to an eShopOnWeb account.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

/// <summary>Request body for <c>POST /customers.json</c>.</summary>
public sealed class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomerAttributes Customer { get; set; } = new();

    /// <summary>
    /// Maxio's duplicate-prevention token. A second POST carrying the same token within 60 minutes
    /// is rejected with <c>409 Conflict</c> instead of being processed again.
    /// </summary>
    [JsonPropertyName("uniqueness_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UniquenessToken { get; set; }
}

public sealed class CreateCustomerAttributes
{
    /// <summary>Required by Maxio; the API rejects a blank given name.</summary>
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Required by Maxio; the API rejects a blank family name.</summary>
    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Must be unique per site; Maxio enforces this, which is what makes signup idempotent.</summary>
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}
