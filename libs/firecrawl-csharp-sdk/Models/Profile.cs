using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Enable persistent browser storage across scrape and interact sessions. Pass a profile when scraping to preserve cookies, localStorage, and session data. Sessions with the same profile name share browser state.
/// </summary>
public record Profile
{
    /// <summary>
    /// A name for the profile. Scrapes with the same name share browser state (cookies, localStorage, sessions).
    /// </summary>
    [JsonPropertyName("name")]
    [StringLength(128, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>
    /// When true, browser state is saved back to the profile when the interact session stops. Set to false to load existing data without writing. Only one saving session is allowed at a time.
    /// </summary>
    [JsonPropertyName("saveChanges")]
    public bool? SaveChanges { get; init; } = true;
}
