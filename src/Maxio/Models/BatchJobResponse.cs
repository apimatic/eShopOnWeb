using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record BatchJobResponse
{
    [JsonPropertyName("batchjob")]
    public required BatchJob Batchjob { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
