using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// The search results. The arrays available will depend on the sources you specified in the request. By default, the <c>web</c> array will be returned.
/// </summary>
public record Data8
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("web")]
    public IReadOnlyList<Web1>? Web { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("images")]
    public IReadOnlyList<Images6>? Images { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("news")]
    public IReadOnlyList<News1>? News { get; init; }
}
