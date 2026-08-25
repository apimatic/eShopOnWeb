using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The details about a saved payment source.
/// </summary>
public record VaultResponse
{
    /// <summary>
    /// The PayPal-generated ID for the saved payment source.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    [StringLength(255, MinimumLength = 1)]
    public string? Id { get; init; }

    /// <summary>
    /// The vault status.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public VaultStatus? Status { get; init; }

    /// <summary>
    /// This object represents a merchant’s customer, allowing them to store contact details, and track all payments associated with the same customer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer")]
    public VaultCustomer? Customer { get; init; }

    /// <summary>
    /// An array of request-related HATEOAS links.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    [MinLength(1)]
    [MaxLength(10)]
    public IReadOnlyList<LinkDescription>? Links { get; init; }
}
