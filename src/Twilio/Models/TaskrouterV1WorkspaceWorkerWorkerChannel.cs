using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record TaskrouterV1WorkspaceWorkerWorkerChannel
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Worker resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The total number of Tasks assigned to Worker for the TaskChannel type.
    /// </summary>
    [JsonPropertyName("assigned_tasks")]
    public int? AssignedTasks { get; init; } = 0;

    /// <summary>
    /// Whether the Worker should receive Tasks of the TaskChannel type.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("available")]
    public bool? Available { get; init; }

    /// <summary>
    /// The current percentage of capacity the TaskChannel has available. Can be a number between <c>0</c> and <c>100</c>. A value of <c>0</c> indicates that TaskChannel has no capacity available and a value of <c>100</c> means the  Worker is available to receive any Tasks of this TaskChannel type.
    /// </summary>
    [JsonPropertyName("available_capacity_percentage")]
    public int? AvailableCapacityPercentage { get; init; } = 0;

    /// <summary>
    /// The current configured capacity for the WorkerChannel. TaskRouter will not create any reservations after the assigned Tasks for the Worker reaches the value.
    /// </summary>
    [JsonPropertyName("configured_capacity")]
    public int? ConfiguredCapacity { get; init; } = 0;

    /// <summary>
    /// The date and time in GMT when the resource was created specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was last updated specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The unique string that we created to identify the WorkerChannel resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WC[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the TaskChannel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("task_channel_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^TC[0-9a-fA-F]{32}$")]
    public string? TaskChannelSid { get; init; }

    /// <summary>
    /// The unique name of the TaskChannel, such as <c>voice</c> or <c>sms</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("task_channel_unique_name")]
    public string? TaskChannelUniqueName { get; init; }

    /// <summary>
    /// The SID of the Worker that contains the WorkerChannel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("worker_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WK[0-9a-fA-F]{32}$")]
    public string? WorkerSid { get; init; }

    /// <summary>
    /// The SID of the Workspace that contains the WorkerChannel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workspace_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? WorkspaceSid { get; init; }

    /// <summary>
    /// The absolute URL of the WorkerChannel resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
