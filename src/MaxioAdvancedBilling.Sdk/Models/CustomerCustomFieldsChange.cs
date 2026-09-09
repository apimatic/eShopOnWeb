using System.Collections.Generic;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record CustomerCustomFieldsChange
{
    [JsonPropertyName("before")]
    public required IReadOnlyList<InvoiceCustomField> Before { get; init; }

    [JsonPropertyName("after")]
    public required IReadOnlyList<InvoiceCustomField> After { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
