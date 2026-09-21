using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record ApiV2010AccountQueueMember
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> the Member resource is associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CA[0-9a-fA-F]{32}$")]
    public string? CallSid { get; init; }

    /// <summary>
    /// The date that the member was enqueued, given in RFC 2822 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_enqueued")]
    public string? DateEnqueued { get; init; }

    /// <summary>
    /// This member's current position in the queue.
    /// </summary>
    [JsonPropertyName("position")]
    public int? Position { get; init; } = 0;

    /// <summary>
    /// The URI of the resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    /// <summary>
    /// The number of seconds the member has been in the queue.
    /// </summary>
    [JsonPropertyName("wait_time")]
    public int? WaitTime { get; init; } = 0;

    /// <summary>
    /// The SID of the Queue the member is in.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("queue_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^QU[0-9a-fA-F]{32}$")]
    public string? QueueSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
