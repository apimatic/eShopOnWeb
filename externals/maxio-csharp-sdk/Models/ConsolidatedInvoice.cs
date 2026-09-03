using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ConsolidatedInvoice
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("invoices")]
    public IReadOnlyList<Invoice>? Invoices { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
