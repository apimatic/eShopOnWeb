using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record WriteText
{
    /// <summary>
    /// Write text into an input field, text area, or contenteditable element. Note: You must first focus the element using a 'click' action before writing. The text will be typed character by character to simulate keyboard input.
    /// </summary>
    [JsonPropertyName("type")]
    public required Type22 Type { get; init; }

    /// <summary>
    /// Text to type
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}
