using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Twilio.Models;

public record CarouselCard
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonPropertyName("media")]
    public required string Media { get; init; }

    [JsonPropertyName("actions")]
    public required IReadOnlyList<CarouselAction> Actions { get; init; }
}
