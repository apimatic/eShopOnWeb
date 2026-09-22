using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record TaskrouterV1WorkspaceWorkerWorkersCumulativeStatistics
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
    /// The minimum, average, maximum, and total time (in seconds) that Workers spent in each Activity.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("activity_durations")]
    public IReadOnlyList<object?>? ActivityDurations { get; init; }

    /// <summary>
    /// The total number of Reservations that were created.
    /// </summary>
    [JsonPropertyName("reservations_created")]
    public int? ReservationsCreated { get; init; } = 0;

    /// <summary>
    /// The total number of Reservations that were accepted.
    /// </summary>
    [JsonPropertyName("reservations_accepted")]
    public int? ReservationsAccepted { get; init; } = 0;

    /// <summary>
    /// The total number of Reservations that were rejected.
    /// </summary>
    [JsonPropertyName("reservations_rejected")]
    public int? ReservationsRejected { get; init; } = 0;

    /// <summary>
    /// The total number of Reservations that were timed out.
    /// </summary>
    [JsonPropertyName("reservations_timed_out")]
    public int? ReservationsTimedOut { get; init; } = 0;

    /// <summary>
    /// The total number of Reservations that were canceled.
    /// </summary>
    [JsonPropertyName("reservations_canceled")]
    public int? ReservationsCanceled { get; init; } = 0;

    /// <summary>
    /// The total number of Reservations that were rescinded.
    /// </summary>
    [JsonPropertyName("reservations_rescinded")]
    public int? ReservationsRescinded { get; init; } = 0;

    /// <summary>
    /// The SID of the Workspace that contains the Workers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workspace_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^WS[0-9a-fA-F]{32}$")]
    public string? WorkspaceSid { get; init; }

    /// <summary>
    /// The absolute URL of the Workers statistics resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
