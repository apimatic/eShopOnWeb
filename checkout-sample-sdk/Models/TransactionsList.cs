using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Validation.Attributes;

namespace PayPalServerSdk.Models;

/// <summary>
/// The list transactions for a subscription request details.
/// </summary>
public record TransactionsList
{
    /// <summary>
    /// An array of transactions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transactions")]
    [MinLength(0)]
    [MaxLength(32767)]
    public IReadOnlyList<SubscriptionTransactionDetails>? Transactions { get; init; }

    /// <summary>
    /// The total number of items.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_items")]
    [Minimum(0)]
    [Maximum(500000000)]
    public int? TotalItems { get; init; }

    /// <summary>
    /// The total number of pages.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total_pages")]
    [Minimum(0)]
    [Maximum(100000000)]
    public int? TotalPages { get; init; }

    /// <summary>
    /// An array of request-related <see href="/docs/api/reference/api-responses/#hateoas-links">HATEOAS links</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    [MinLength(1)]
    [MaxLength(10)]
    public IReadOnlyList<LinkDescription>? Links { get; init; }
}
