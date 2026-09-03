using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// Example schema for an <c>change_invoice_collection_method</c> event
/// </summary>
public record ChangeInvoiceCollectionMethodEventData
{
    /// <summary>
    /// The previous collection method of the invoice.
    /// </summary>
    [JsonPropertyName("from_collection_method")]
    public required string FromCollectionMethod { get; init; }

    /// <summary>
    /// The new collection method of the invoice.
    /// </summary>
    [JsonPropertyName("to_collection_method")]
    public required string ToCollectionMethod { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
