using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Web
{
    [JsonPropertyName("type")]
    public required Type40 Type { get; init; }

    /// <summary>
    /// Time-based search parameter. Supports predefined time ranges (<c>qdr:h</c>, <c>qdr:d</c>, <c>qdr:w</c>, <c>qdr:m</c>, <c>qdr:y</c>), custom date ranges (<c>cdr:1,cd_min:MM/DD/YYYY,cd_max:MM/DD/YYYY</c>), and sort by date (<c>sbd:1</c>). Values can be combined, e.g. <c>sbd:1,qdr:w</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tbs")]
    public string? Tbs { get; init; }

    /// <summary>
    /// Location parameter for search results
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("location")]
    public string? Location { get; init; }
}
