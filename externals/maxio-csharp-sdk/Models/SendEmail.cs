using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SendEmail
{
    [JsonPropertyName("can_execute")]
    public required bool CanExecute { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
