using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Twilio.Models;

public record FlowsPage
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_page_id")]
    public string? NextPageId { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("layout")]
    public required IReadOnlyList<FlowsPageComponent> Layout { get; init; }
}
