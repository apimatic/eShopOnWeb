using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record TaskrouterV1WorkspaceTask
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Task resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The number of seconds since the Task was created.
    /// </summary>
    [JsonPropertyName("age")]
    public int? Age { get; init; } = 0;

    /// <summary>
    /// The current status of the Task's assignment. Can be: <c>pending</c>, <c>reserved</c>, <c>assigned</c>, <c>canceled</c>, <c>wrapping</c>, or <c>completed</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("assignment_status")]
    public TaskEnumStatus? AssignmentStatus { get; init; }

    /// <summary>
    /// The JSON string with custom attributes of the work. <b>Note</b> If this property has been assigned a value, it will only be displayed in FETCH action that returns a single resource. Otherwise, it will be null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("attributes")]
    public string? Attributes { get; init; }

    /// <summary>
    /// An object that contains the <see href="https://www.twilio.com/docs/add-ons">Add-on</see> data for all installed Add-ons.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("addons")]
    public string? Addons { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was created specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was last updated specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The date and time in GMT when the Task entered the TaskQueue, specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("task_queue_entered_date")]
    public DateTimeOffset? TaskQueueEnteredDate { get; init; }

    /// <summary>
    /// The current priority score of the Task as assigned to a Worker by the workflow. Tasks with higher priority values will be assigned before Tasks with lower values.
    /// </summary>
    [JsonPropertyName("priority")]
    public int? Priority { get; init; } = 0;

    /// <summary>
    /// The reason the Task was canceled or completed, if applicable.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>
    /// The unique string that we created to identify the Task resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WT[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the TaskQueue.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("task_queue_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WQ[0-9a-fA-F]{32}$")]
    public string? TaskQueueSid { get; init; }

    /// <summary>
    /// The friendly name of the TaskQueue.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("task_queue_friendly_name")]
    public string? TaskQueueFriendlyName { get; init; }

    /// <summary>
    /// The SID of the TaskChannel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("task_channel_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^TC[0-9a-fA-F]{32}$")]
    public string? TaskChannelSid { get; init; }

    /// <summary>
    /// The unique name of the TaskChannel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("task_channel_unique_name")]
    public string? TaskChannelUniqueName { get; init; }

    /// <summary>
    /// The amount of time in seconds that the Task can live before being assigned.
    /// </summary>
    [JsonPropertyName("timeout")]
    public int? Timeout { get; init; } = 0;

    /// <summary>
    /// The SID of the Workflow that is controlling the Task.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workflow_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WW[0-9a-fA-F]{32}$")]
    public string? WorkflowSid { get; init; }

    /// <summary>
    /// The friendly name of the Workflow that is controlling the Task.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workflow_friendly_name")]
    public string? WorkflowFriendlyName { get; init; }

    /// <summary>
    /// The SID of the Workspace that contains the Task.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workspace_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? WorkspaceSid { get; init; }

    /// <summary>
    /// The absolute URL of the Task resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The URLs of related resources.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    /// <summary>
    /// The date and time in GMT indicating the ordering for routing of the Task specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("virtual_start_time")]
    public DateTimeOffset? VirtualStartTime { get; init; }

    /// <summary>
    /// A boolean that indicates if the Task should respect a Worker's capacity and availability during assignment. This field can only be used when the <c>RoutingTarget</c> field is set to a Worker SID. By setting <c>IgnoreCapacity</c> to a value of <c>true</c>, <c>1</c>, or <c>yes</c>, the Task will be routed to the Worker without respecting their capacity and availability. Any other value will enforce the Worker's capacity and availability. The default value of <c>IgnoreCapacity</c> is <c>true</c> when the <c>RoutingTarget</c> is set to a Worker SID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ignore_capacity")]
    public bool? IgnoreCapacity { get; init; }

    /// <summary>
    /// A SID of a Worker, Queue, or Workflow to route a Task to
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("routing_target")]
    public string? RoutingTarget { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
