using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record GeneratePdf
{
    /// <summary>
    /// Generate a PDF of the current page. The PDF will be returned in the <c>actions.pdfs</c> array of the response.
    /// </summary>
    [JsonPropertyName("type")]
    public required Type27 Type { get; init; }

    /// <summary>
    /// The page size of the resulting PDF
    /// </summary>
    [JsonPropertyName("format")]
    public Format? Format { get; init; } = Format.Letter;

    /// <summary>
    /// Whether to generate the PDF in landscape orientation
    /// </summary>
    [JsonPropertyName("landscape")]
    public bool? Landscape { get; init; } = false;

    /// <summary>
    /// The scale multiplier of the resulting PDF
    /// </summary>
    [JsonPropertyName("scale")]
    public double? Scale { get; init; } = 1d;
}
