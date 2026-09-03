using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record BulkComponentsPricePointAssignment
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("components")]
    public IReadOnlyList<ComponentPricePointAssignment>? Components { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
