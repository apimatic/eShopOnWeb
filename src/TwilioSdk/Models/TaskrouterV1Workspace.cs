using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record TaskrouterV1Workspace
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
    /// The name of the default activity.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_activity_name")]
    public string? DefaultActivityName { get; init; }

    /// <summary>
    /// The SID of the Activity that will be used when new Workers are created in the Workspace.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_activity_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WA[0-9a-fA-F]{32}$")]
    public string? DefaultActivitySid { get; init; }

    /// <summary>
    /// The URL we call when an event occurs. If provided, the Workspace will publish events to this URL, for example, to collect data for reporting. See <see href="https://www.twilio.com/docs/taskrouter/api/event">Workspace Events</see> for more information. This parameter supports Twilio's <see href="https://www.twilio.com/docs/usage/webhooks/webhooks-connection-overrides">Webhooks (HTTP callbacks) Connection Overrides</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("event_callback_url")]
    [Format(FormatKind.Uri)]
    public string? EventCallbackUrl { get; init; }

    /// <summary>
    /// The list of Workspace events for which to call <c>event_callback_url</c>. For example, if <c>EventsFilter=task.created, task.canceled, worker.activity.update</c>, then TaskRouter will call event_callback_url only when a task is created, canceled, or a Worker activity is updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("events_filter")]
    public string? EventsFilter { get; init; }

    /// <summary>
    /// The string that you assigned to describe the Workspace resource. For example <c>Customer Support</c> or <c>2014 Election Campaign</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// Whether multi-tasking is enabled. The default is <c>true</c>, which enables multi-tasking. Multi-tasking allows Workers to handle multiple Tasks simultaneously. When enabled (<c>true</c>), each Worker can receive parallel reservations up to the per-channel maximums defined in the Workers section. In single-tasking each Worker would only receive a new reservation when the previous task is completed. Learn more at <see href="https://www.twilio.com/docs/taskrouter/multitasking">Multitasking</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("multi_task_enabled")]
    public bool? MultiTaskEnabled { get; init; }

    /// <summary>
    /// The unique string that we created to identify the Workspace resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The name of the timeout activity.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("timeout_activity_name")]
    public string? TimeoutActivityName { get; init; }

    /// <summary>
    /// The SID of the Activity that will be assigned to a Worker when a Task reservation times out without a response.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("timeout_activity_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WA[0-9a-fA-F]{32}$")]
    public string? TimeoutActivitySid { get; init; }

    /// <summary>
    /// The type of TaskQueue to prioritize when Workers are receiving Tasks from both types of TaskQueues. Can be: <c>LIFO</c> or <c>FIFO</c> and the default is <c>FIFO</c>. For more information, see <see href="https://www.twilio.com/docs/taskrouter/queue-ordering-last-first-out-lifo">Queue Ordering</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("prioritize_queue_order")]
    public WorkspaceEnumQueueOrder? PrioritizeQueueOrder { get; init; }

    /// <summary>
    /// The absolute URL of the Workspace resource.
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

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
