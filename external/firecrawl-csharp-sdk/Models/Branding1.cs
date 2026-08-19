using System.Collections.Generic;
using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Branding information extracted from the page if <c>branding</c> is in <c>formats</c>. Includes colors, fonts, typography, spacing, components, and more.
/// </summary>
public record Branding1
{
    /// <summary>
    /// The detected color scheme of the page.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("colorScheme")]
    public ColorScheme? ColorScheme { get; init; }

    /// <summary>
    /// URL of the primary logo.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("logo")]
    public string? Logo { get; init; }

    /// <summary>
    /// Brand colors extracted from the page.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("colors")]
    public Colors? Colors { get; init; }

    /// <summary>
    /// Array of font families used on the page.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fonts")]
    public IReadOnlyList<Font?>? Fonts { get; init; }

    /// <summary>
    /// Detailed typography information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("typography")]
    public Typography? Typography { get; init; }

    /// <summary>
    /// Spacing and layout information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("spacing")]
    public Spacing? Spacing { get; init; }

    /// <summary>
    /// UI component styles.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("components")]
    public Components? Components { get; init; }

    /// <summary>
    /// Icon style information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("icons")]
    public object? Icons { get; init; }

    /// <summary>
    /// Brand images.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("images")]
    public Images2? Images { get; init; }

    /// <summary>
    /// Animation and transition settings.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("animations")]
    public object? Animations { get; init; }

    /// <summary>
    /// Layout configuration (grid, header/footer heights).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("layout")]
    public object? Layout { get; init; }

    /// <summary>
    /// Brand personality traits (tone, energy, target audience).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("personality")]
    public object? Personality { get; init; }
}
