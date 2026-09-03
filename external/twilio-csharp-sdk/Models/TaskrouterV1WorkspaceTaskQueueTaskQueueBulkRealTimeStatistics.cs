using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record TaskrouterV1WorkspaceTaskQueueTaskQueueBulkRealTimeStatistics
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
    /// The SID of the Workspace that contains the TaskQueue.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workspace_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? WorkspaceSid { get; init; }

    /// <summary>
    /// The real-time statistics for each requested TaskQueue SID. <c>task_queue_data</c> returns the following attributes:
    /// <para>
    /// <c>task_queue_sid</c>: The SID of the TaskQueue from which these statistics were calculated.
    /// </para>
    /// <para>
    /// <c>total_available_workers</c>: The total number of Workers available for Tasks in the TaskQueue.
    /// </para>
    /// <para>
    /// <c>total_eligible_workers</c>: The total number of Workers eligible for Tasks in the TaskQueue, regardless of their Activity state.
    /// </para>
    /// <para>
    /// <c>total_tasks</c>: The total number of Tasks.
    /// </para>
    /// <para>
    /// <c>longest_task_waiting_age</c>: The age of the longest waiting Task.
    /// </para>
    /// <para>
    /// <c>longest_task_waiting_sid</c>: The SID of the longest waiting Task.
    /// </para>
    /// <para>
    /// <c>tasks_by_status</c>: The number of Tasks grouped by their current status.
    /// </para>
    /// <para>
    /// <c>tasks_by_priority</c>: The number of Tasks grouped by priority.
    /// </para>
    /// <para>
    /// <c>activity_statistics</c>: The number of current Workers grouped by Activity.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("task_queue_data")]
    public IReadOnlyList<object?>? TaskQueueData { get; init; }

    /// <summary>
    /// The number of TaskQueue statistics received in task_queue_data.
    /// </summary>
    [JsonPropertyName("task_queue_response_count")]
    public int? TaskQueueResponseCount { get; init; } = 0;

    /// <summary>
    /// The absolute URL of the TaskQueue statistics resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
