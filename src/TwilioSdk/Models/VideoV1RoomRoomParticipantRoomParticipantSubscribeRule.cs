using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record VideoV1RoomRoomParticipantRoomParticipantSubscribeRule
{
    /// <summary>
    /// The SID of the Participant resource for the Subscribe Rules.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("participant_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PA[0-9a-fA-F]{32}$")]
    public string? ParticipantSid { get; init; }

    /// <summary>
    /// The SID of the Room resource for the Subscribe Rules
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("room_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RM[0-9a-fA-F]{32}$")]
    public string? RoomSid { get; init; }

    /// <summary>
    /// A collection of Subscribe Rules that describe how to include or exclude matching tracks. See the <see href="https://www.twilio.com/docs/video/api/track-subscriptions#specifying-sr">Specifying Subscribe Rules</see> section for further information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rules")]
    public IReadOnlyList<Rule?>? Rules { get; init; }

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

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
