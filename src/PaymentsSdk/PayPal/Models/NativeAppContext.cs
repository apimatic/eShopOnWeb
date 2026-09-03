using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// Merchant provided, buyer's native app preferences to app switch to the PayPal consumer app.
/// </summary>
public record NativeAppContext
{
    /// <summary>
    /// Operating System type of the device that the buyer is using.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("os_type")]
    public OsType? OsType { get; init; }

    /// <summary>
    /// Operating System version of the device that the buyer is using.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("os_version")]
    [StringLength(64, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public string? OsVersion { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
