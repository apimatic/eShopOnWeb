using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record IssueAdvanceInvoiceRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("force")]
    public bool? Force { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
