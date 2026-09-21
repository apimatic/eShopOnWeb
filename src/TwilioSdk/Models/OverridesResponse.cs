using System;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record OverridesResponse
{
    /// <summary>
    /// The phone number for which the override was created
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// The original line type
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("original_line_type")]
    public OriginalLineType? OriginalLineType { get; init; }

    /// <summary>
    /// The new line type after the override
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("overridden_line_type")]
    public OverriddenLineType? OverriddenLineType { get; init; }

    /// <summary>
    /// The reason for the override
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("override_reason")]
    public string? OverrideReason { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("override_timestamp")]
    public DateTimeOffset? OverrideTimestamp { get; init; }

    /// <summary>
    /// The Account SID for the user who made the override
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("overridden_by_account_sid")]
    public string? OverriddenByAccountSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
