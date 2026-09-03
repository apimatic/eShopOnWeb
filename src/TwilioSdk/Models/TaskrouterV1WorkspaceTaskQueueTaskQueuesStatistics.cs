using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record TaskrouterV1WorkspaceTaskQueueTaskQueuesStatistics
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the TaskQueue resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// An object that contains the cumulative statistics for the TaskQueues.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cumulative")]
    public object? Cumulative { get; init; }

    /// <summary>
    /// An object that contains the real-time statistics for the TaskQueues.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("realtime")]
    public object? Realtime { get; init; }

    /// <summary>
    /// The SID of the TaskQueue from which these statistics were calculated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("task_queue_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WQ[0-9a-fA-F]{32}$")]
    public string? TaskQueueSid { get; init; }

    /// <summary>
    /// The SID of the Workspace that contains the TaskQueues.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workspace_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? WorkspaceSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
