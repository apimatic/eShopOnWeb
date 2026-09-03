using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// Request payload for updating a draft ad hoc invoice.
/// </summary>
public record UpdateInvoiceRequest
{
    /// <summary>
    /// Attributes of a draft ad hoc invoice which can be updated. Only the submitted attributes are changed.
    /// </summary>
    [JsonPropertyName("invoice")]
    public required UpdateInvoice Invoice { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
