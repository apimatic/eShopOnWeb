using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

// Wire contracts for the Maxio Billing API. Only the fields this integration actually reads or
// writes are modelled; Maxio returns a great deal more and unknown members are simply ignored.

internal sealed class MaxioCustomer
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

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>Maxio wraps single resources in a one-property envelope, e.g. { "customer": { ... } }.</summary>
internal sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomerAttributes Customer { get; set; } = new();

    /// <summary>
    /// Duplicate-prevention token. A repeat of the same POST within 60 minutes is rejected with
    /// 409 instead of creating a second record.
    /// </summary>
    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioCreateCustomerAttributes
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}
