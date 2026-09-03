using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record FlexV1InteractionInteractionChannelInteractionChannelInvite
{
    /// <summary>
    /// The unique string created by Twilio to identify an Interaction Channel Invite resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KG[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The Interaction SID for this Channel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("interaction_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KD[0-9a-fA-F]{32}$")]
    public string? InteractionSid { get; init; }

    /// <summary>
    /// The Channel SID for this Invite.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channel_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^UO[0-9a-fA-F]{32}$")]
    public string? ChannelSid { get; init; }

    /// <summary>
    /// A JSON object representing the routing rules for the Interaction Channel. See <see href="https://www.twilio.com/docs/flex/developer/conversations/interactions-api/interactions#agent-initiated-outbound-interactions">Outbound SMS Example</see> for an example Routing object. The Interactions resource uses TaskRouter for all routing functionality.
    ///  All attributes in the Routing object on your Interaction request body are added “as is” to the task. For a list of known attributes consumed by the Flex UI and/or Flex Insights, see <see href="https://www.twilio.com/docs/flex/developer/conversations/interactions-api#task-attributes">Known Task Attributes</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("routing")]
    public object? Routing { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
