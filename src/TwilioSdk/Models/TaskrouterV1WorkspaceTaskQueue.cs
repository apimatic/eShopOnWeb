using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record TaskrouterV1WorkspaceTaskQueue
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
    /// The SID of the Activity to assign Workers when a task is assigned for them.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("assignment_activity_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WA[0-9a-fA-F]{32}$")]
    public string? AssignmentActivitySid { get; init; }

    /// <summary>
    /// The name of the Activity to assign Workers when a task is assigned for them.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("assignment_activity_name")]
    public string? AssignmentActivityName { get; init; }

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
    /// The string that you assigned to describe the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The maximum number of Workers to reserve for the assignment of a task in the queue. Can be an integer between 1 and 50, inclusive and defaults to 1.
    /// </summary>
    [JsonPropertyName("max_reserved_workers")]
    public int? MaxReservedWorkers { get; init; } = 0;

    /// <summary>
    /// The SID of the Activity to assign Workers once a task is reserved for them.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reservation_activity_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WA[0-9a-fA-F]{32}$")]
    public string? ReservationActivitySid { get; init; }

    /// <summary>
    /// The name of the Activity to assign Workers once a task is reserved for them.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reservation_activity_name")]
    public string? ReservationActivityName { get; init; }

    /// <summary>
    /// The unique string that we created to identify the TaskQueue resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WQ[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// A string describing the Worker selection criteria for any Tasks that enter the TaskQueue. For example <c>'"language" == "spanish"'</c> If no TargetWorkers parameter is provided, Tasks will wait in the TaskQueue until they are either deleted or moved to another TaskQueue. Additional examples on how to describing Worker selection criteria below. Defaults to 1==1.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("target_workers")]
    public string? TargetWorkers { get; init; }

    /// <summary>
    /// How Tasks will be assigned to Workers. Set this parameter to <c>LIFO</c> to assign most recently created Task first or <c>FIFO</c> to assign the oldest Task. Default is FIFO. <see href="https://www.twilio.com/docs/taskrouter/queue-ordering-last-first-out-lifo">Click here</see> to learn more.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("task_order")]
    public TaskQueueEnumTaskOrder? TaskOrder { get; init; }

    /// <summary>
    /// The absolute URL of the TaskQueue resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The SID of the Workspace that contains the TaskQueue.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workspace_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? WorkspaceSid { get; init; }

    /// <summary>
    /// The URLs of related resources.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
