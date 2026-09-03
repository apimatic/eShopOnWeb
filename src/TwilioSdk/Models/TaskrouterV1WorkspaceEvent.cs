using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record TaskrouterV1WorkspaceEvent
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Event resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The SID of the resource that triggered the event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("actor_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^[a-zA-Z]{2}[0-9a-fA-F]{32}$")]
    public string? ActorSid { get; init; }

    /// <summary>
    /// The type of resource that triggered the event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("actor_type")]
    public string? ActorType { get; init; }

    /// <summary>
    /// The absolute URL of the resource that triggered the event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("actor_url")]
    [Format(FormatKind.Uri)]
    public string? ActorUrl { get; init; }

    /// <summary>
    /// A description of the event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Data about the event. For more information, see <see href="https://www.twilio.com/docs/taskrouter/api/event#event-types">Event types</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("event_data")]
    public object? EventData { get; init; }

    /// <summary>
    /// The time the event was sent, specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("event_date")]
    public DateTimeOffset? EventDate { get; init; }

    /// <summary>
    /// The time the event was sent in milliseconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("event_date_ms")]
    public long? EventDateMs { get; init; }

    /// <summary>
    /// The identifier for the event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("event_type")]
    public string? EventType { get; init; }

    /// <summary>
    /// The SID of the object the event is most relevant to, such as a TaskSid, ReservationSid, or a  WorkerSid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("resource_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^[a-zA-Z]{2}[0-9a-fA-F]{32}$")]
    public string? ResourceSid { get; init; }

    /// <summary>
    /// The type of object the event is most relevant to, such as a Task, Reservation, or a Worker).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("resource_type")]
    public string? ResourceType { get; init; }

    /// <summary>
    /// The URL of the resource the event is most relevant to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("resource_url")]
    [Format(FormatKind.Uri)]
    public string? ResourceUrl { get; init; }

    /// <summary>
    /// The unique string that we created to identify the Event resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^EV[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// Where the Event originated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>
    /// The IP from which the Event originated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("source_ip_address")]
    public string? SourceIpAddress { get; init; }

    /// <summary>
    /// The absolute URL of the Event resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The SID of the Workspace that contains the Event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workspace_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? WorkspaceSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
