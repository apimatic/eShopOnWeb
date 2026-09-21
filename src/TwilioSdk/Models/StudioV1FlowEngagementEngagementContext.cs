using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record StudioV1FlowEngagementEngagementContext
{
    /// <summary>
    /// The SID of the Account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// As your flow executes, we save the state in what's called the Flow Context. Any data in the flow context can be accessed by your widgets as variables, either in configuration fields or in text areas as variable substitution.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("context")]
    public object? Context { get; init; }

    /// <summary>
    /// The SID of the Engagement.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("engagement_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^FN[0-9a-fA-F]{32}$")]
    public string? EngagementSid { get; init; }

    /// <summary>
    /// The SID of the Flow.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flow_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^FW[0-9a-fA-F]{32}$")]
    public string? FlowSid { get; init; }

    /// <summary>
    /// The URL of the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
