using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record AvailableActions
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("send_email")]
    public SendEmail? SendEmail { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
