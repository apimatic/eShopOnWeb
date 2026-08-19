using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Enable persistent storage across interact sessions. Data saved in one session can be loaded in a later session using the same name.
/// </summary>
public record Profile1
{
    /// <summary>
    /// A name for the profile. Sessions with the same name share storage.
    /// </summary>
    [JsonPropertyName("name")]
    [StringLength(128, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>
    /// When true, browser state is saved back to the profile on close. Set to false to load existing data without writing. Multiple non-saving sessions are allowed but only one saving session at a time.
    /// </summary>
    [JsonPropertyName("saveChanges")]
    public bool? SaveChanges { get; init; } = true;
}
