using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record TaskrouterV1WorkspaceWorkflowWorkflowStatistics
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Workflow resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// An object that contains the cumulative statistics for the Workflow.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cumulative")]
    public object? Cumulative { get; init; }

    /// <summary>
    /// An object that contains the real-time statistics for the Workflow.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("realtime")]
    public object? Realtime { get; init; }

    /// <summary>
    /// Returns the list of Tasks that are being controlled by the Workflow with the specified SID value.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workflow_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WW[0-9a-fA-F]{32}$")]
    public string? WorkflowSid { get; init; }

    /// <summary>
    /// The SID of the Workspace that contains the Workflow.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workspace_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? WorkspaceSid { get; init; }

    /// <summary>
    /// The absolute URL of the Workflow statistics resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
