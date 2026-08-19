using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record Parser1
{
    [JsonPropertyName("type")]
    public required Type17 Type { get; init; }

    /// <summary>
    /// PDF parsing mode. "fast": text-only extraction. "auto": text-first with OCR fallback. "ocr": OCR on every page.
    /// </summary>
    [JsonPropertyName("mode")]
    public Mode4? Mode { get; init; } = Mode4.Auto;

    /// <summary>
    /// Maximum number of pages to parse from the PDF.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxPages")]
    [Minimum(1)]
    [Maximum(10000)]
    public int? MaxPages { get; init; }
}
