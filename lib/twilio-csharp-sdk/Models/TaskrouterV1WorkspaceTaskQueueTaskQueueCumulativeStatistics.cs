using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record TaskrouterV1WorkspaceTaskQueueTaskQueueCumulativeStatistics
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
    /// The average time in seconds between Task creation and acceptance.
    /// </summary>
    [JsonPropertyName("avg_task_acceptance_time")]
    public int? AvgTaskAcceptanceTime { get; init; } = 0;

    /// <summary>
    /// The beginning of the interval during which these statistics were calculated, in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_time")]
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>
    /// The end of the interval during which these statistics were calculated, in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_time")]
    public DateTimeOffset? EndTime { get; init; }

    /// <summary>
    /// The total number of Reservations created for Tasks in the TaskQueue.
    /// </summary>
    [JsonPropertyName("reservations_created")]
    public int? ReservationsCreated { get; init; } = 0;

    /// <summary>
    /// The total number of Reservations accepted for Tasks in the TaskQueue.
    /// </summary>
    [JsonPropertyName("reservations_accepted")]
    public int? ReservationsAccepted { get; init; } = 0;

    /// <summary>
    /// The total number of Reservations rejected for Tasks in the TaskQueue.
    /// </summary>
    [JsonPropertyName("reservations_rejected")]
    public int? ReservationsRejected { get; init; } = 0;

    /// <summary>
    /// The total number of Reservations that timed out for Tasks in the TaskQueue.
    /// </summary>
    [JsonPropertyName("reservations_timed_out")]
    public int? ReservationsTimedOut { get; init; } = 0;

    /// <summary>
    /// The total number of Reservations canceled for Tasks in the TaskQueue.
    /// </summary>
    [JsonPropertyName("reservations_canceled")]
    public int? ReservationsCanceled { get; init; } = 0;

    /// <summary>
    /// The total number of Reservations rescinded.
    /// </summary>
    [JsonPropertyName("reservations_rescinded")]
    public int? ReservationsRescinded { get; init; } = 0;

    /// <summary>
    /// A list of objects that describe the number of Tasks canceled and reservations accepted above and below the thresholds specified in seconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("split_by_wait_time")]
    public object? SplitByWaitTime { get; init; }

    /// <summary>
    /// The SID of the TaskQueue from which these statistics were calculated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("task_queue_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WQ[0-9a-fA-F]{32}$")]
    public string? TaskQueueSid { get; init; }

    /// <summary>
    /// The wait duration statistics (<c>avg</c>, <c>min</c>, <c>max</c>, <c>total</c>) for Tasks accepted while in the TaskQueue. Calculation is based on the time when the Tasks were created. For transfers, the wait duration is counted from the moment <b>*the Task was created</b>*, and not from when the transfer was initiated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("wait_duration_until_accepted")]
    public object? WaitDurationUntilAccepted { get; init; }

    /// <summary>
    /// The wait duration statistics (<c>avg</c>, <c>min</c>, <c>max</c>, <c>total</c>) for Tasks canceled while in the TaskQueue.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("wait_duration_until_canceled")]
    public object? WaitDurationUntilCanceled { get; init; }

    /// <summary>
    /// The relative wait duration statistics (<c>avg</c>, <c>min</c>, <c>max</c>, <c>total</c>) for Tasks accepted while in the TaskQueue. Calculation is based on the time when the Tasks entered the TaskQueue.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("wait_duration_in_queue_until_accepted")]
    public object? WaitDurationInQueueUntilAccepted { get; init; }

    /// <summary>
    /// The total number of Tasks canceled in the TaskQueue.
    /// </summary>
    [JsonPropertyName("tasks_canceled")]
    public int? TasksCanceled { get; init; } = 0;

    /// <summary>
    /// The total number of Tasks completed in the TaskQueue.
    /// </summary>
    [JsonPropertyName("tasks_completed")]
    public int? TasksCompleted { get; init; } = 0;

    /// <summary>
    /// The total number of Tasks deleted in the TaskQueue.
    /// </summary>
    [JsonPropertyName("tasks_deleted")]
    public int? TasksDeleted { get; init; } = 0;

    /// <summary>
    /// The total number of Tasks entered into the TaskQueue.
    /// </summary>
    [JsonPropertyName("tasks_entered")]
    public int? TasksEntered { get; init; } = 0;

    /// <summary>
    /// The total number of Tasks that were moved from one queue to another.
    /// </summary>
    [JsonPropertyName("tasks_moved")]
    public int? TasksMoved { get; init; } = 0;

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
