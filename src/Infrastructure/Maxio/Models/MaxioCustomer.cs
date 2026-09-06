using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Maxio <c>Customer</c> (<c>maxio-spec/components/schemas/Customer.yaml</c>).
/// </summary>
/// <remarks>
/// Only the properties eShopOnWeb reads are transcribed. Unknown properties in a Maxio response are
/// ignored rather than rejected, so a provider-side addition never breaks a running deployment.
/// </remarks>
public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    /// <summary>The unique identifier used within your own application for this customer.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}
