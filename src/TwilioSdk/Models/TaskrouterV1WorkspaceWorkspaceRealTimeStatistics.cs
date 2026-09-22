using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record TaskrouterV1WorkspaceWorkspaceRealTimeStatistics
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Workspace resource.
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
    /// The total number of Tasks.
    /// </summary>
    [JsonPropertyName("total_tasks")]
    public int? TotalTasks { get; init; } = 0;

    /// <summary>
    /// The total number of Workers in the Workspace.
    /// </summary>
    [JsonPropertyName("total_workers")]
    public int? TotalWorkers { get; init; } = 0;

    /// <summary>
    /// The SID of the Workspace.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workspace_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? WorkspaceSid { get; init; }

    /// <summary>
    /// The absolute URL of the Workspace statistics resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
