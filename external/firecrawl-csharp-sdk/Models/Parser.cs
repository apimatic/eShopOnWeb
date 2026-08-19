using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Parser
{
    [JsonPropertyName("type")]
    public required Type17 Type { get; init; }

    /// <summary>
    /// PDF parsing mode. "fast": text-based extraction only (embedded text, fastest). "auto" (default): attempts fast extraction first, falls back to OCR if needed. "ocr": forces OCR parsing on every page.
    /// </summary>
    [JsonPropertyName("mode")]
    public Mode1? Mode { get; init; } = Mode1.Auto;

    /// <summary>
    /// Maximum number of pages to parse from the PDF. Must be a positive integer up to 10000.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxPages")]
    [Minimum(1)]
    [Maximum(10000)]
    public int? MaxPages { get; init; }
}
