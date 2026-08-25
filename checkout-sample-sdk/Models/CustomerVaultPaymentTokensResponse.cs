using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Validation.Attributes;

namespace PayPalServerSdk.Models;

/// <summary>
/// Collection of payment tokens saved for a given customer.
/// </summary>
public record CustomerVaultPaymentTokensResponse
{
    /// <summary>
    /// Total number of items.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_items")]
    [Minimum(1)]
    [Maximum(50)]
    public int? TotalItems { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_pages")]
    [Minimum(1)]
    [Maximum(10)]
    public int? TotalPages { get; init; }

    /// <summary>
    /// This object defines a customer in your system. Use it to manage customer profiles, save payment methods and contact details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer")]
    public VaultResponseCustomer? Customer { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_tokens")]
    [MinLength(0)]
    [MaxLength(64)]
    public IReadOnlyList<PaymentTokenResponse>? PaymentTokens { get; init; }

    /// <summary>
    /// An array of related <see href="/api/rest/responses/#hateoas">HATEOAS links</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    [MinLength(1)]
    [MaxLength(32)]
    public IReadOnlyList<LinkDescription>? Links { get; init; }
}
