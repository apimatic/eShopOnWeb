using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

/// <summary>
/// Attributes of the target customer who will be the responsible payer of the created subscription. Required.
/// </summary>
public record GroupTarget
{
    /// <summary>
    /// The type of object indicated by the id attribute.
    /// </summary>
    [JsonPropertyName("type")]
    public required GroupTargetType Type { get; init; }

    /// <summary>
    /// The id of the target customer or subscription to group the existing subscription with. Ignored and should not be included if type is "self", "parent", or "eldest".
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
