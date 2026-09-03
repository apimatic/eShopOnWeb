using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record TaskrouterV1WorkspaceTaskQueueTaskQueueRealTimeStatistics
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
    /// The number of current Workers by Activity.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("activity_statistics")]
    public IReadOnlyList<object?>? ActivityStatistics { get; init; }

    /// <summary>
    /// The age of the longest waiting Task.
    /// </summary>
    [JsonPropertyName("longest_task_waiting_age")]
    public int? LongestTaskWaitingAge { get; init; } = 0;

    /// <summary>
    /// The SID of the longest waiting Task.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("longest_task_waiting_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WT[0-9a-fA-F]{32}$")]
    public string? LongestTaskWaitingSid { get; init; }

    /// <summary>
    /// The relative age in the TaskQueue for the longest waiting Task. Calculation is based on the time when the Task entered the TaskQueue.
    /// </summary>
    [JsonPropertyName("longest_relative_task_age_in_queue")]
    public int? LongestRelativeTaskAgeInQueue { get; init; } = 0;

    /// <summary>
    /// The Task SID of the Task waiting in the TaskQueue the longest. Calculation is based on the time when the Task entered the TaskQueue.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("longest_relative_task_sid_in_queue")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WT[0-9a-fA-F]{32}$")]
    public string? LongestRelativeTaskSidInQueue { get; init; }

    /// <summary>
    /// The SID of the TaskQueue from which these statistics were calculated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("task_queue_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WQ[0-9a-fA-F]{32}$")]
    public string? TaskQueueSid { get; init; }

    /// <summary>
    /// The number of Tasks by priority. For example: <c>{"0": "10", "99": "5"}</c> shows 10 Tasks at priority 0 and 5 at priority 99.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tasks_by_priority")]
    public object? TasksByPriority { get; init; }

    /// <summary>
    /// The number of Tasks by their current status. For example: <c>{"pending": "1", "reserved": "3", "assigned": "2", "completed": "5"}</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tasks_by_status")]
    public object? TasksByStatus { get; init; }

    /// <summary>
    /// The total number of Workers in the TaskQueue with an <c>available</c> status. Workers with an <c>available</c> status may already have active interactions or may have none.
    /// </summary>
    [JsonPropertyName("total_available_workers")]
    public int? TotalAvailableWorkers { get; init; } = 0;

    /// <summary>
    /// The total number of Workers eligible for Tasks in the TaskQueue, independent of their Activity state.
    /// </summary>
    [JsonPropertyName("total_eligible_workers")]
    public int? TotalEligibleWorkers { get; init; } = 0;

    /// <summary>
    /// The total number of Tasks.
    /// </summary>
    [JsonPropertyName("total_tasks")]
    public int? TotalTasks { get; init; } = 0;

    /// <summary>
    /// The SID of the Workspace that contains the TaskQueue.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workspace_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? WorkspaceSid { get; init; }

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
