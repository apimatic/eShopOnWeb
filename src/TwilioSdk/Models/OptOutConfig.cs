using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record OptOutConfig
{
    /// <summary>
    /// The unique SID identifier for the opt-out configuration
    /// </summary>
    [JsonPropertyName("opt_out_sid")]
    [RegularExpression("^OO[0-9a-fA-F]{32}$")]
    public required string OptOutSid { get; init; }

    /// <summary>
    /// The SID of the account that owns this opt-out configuration
    /// </summary>
    [JsonPropertyName("account_sid")]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public required string AccountSid { get; init; }

    /// <summary>
    /// A human-readable name for the opt-out configuration
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The date and time when the opt-out configuration was created
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time when the opt-out configuration was last updated
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
