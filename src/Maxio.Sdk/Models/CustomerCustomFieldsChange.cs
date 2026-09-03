using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CustomerCustomFieldsChange
{
    [JsonPropertyName("before")]
    public required IReadOnlyList<InvoiceCustomField> Before { get; init; }

    [JsonPropertyName("after")]
    public required IReadOnlyList<InvoiceCustomField> After { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
