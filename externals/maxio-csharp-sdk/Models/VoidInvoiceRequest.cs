using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record VoidInvoiceRequest
{
    [JsonPropertyName("void")]
    public required VoidInvoice Void { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
