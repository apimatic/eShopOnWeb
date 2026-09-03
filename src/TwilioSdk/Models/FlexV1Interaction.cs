using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record FlexV1Interaction
{
    /// <summary>
    /// The unique string created by Twilio to identify an Interaction resource, prefixed with KD.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KD[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// A JSON object that defines the Interaction’s communication channel and includes details about the channel. See the <see href="https://www.twilio.com/docs/flex/developer/conversations/interactions-api/interactions#agent-initiated-outbound-interactions">Outbound SMS</see> and <see href="https://www.twilio.com/docs/flex/developer/conversations/interactions-api/interactions#api-initiated-contact">inbound (API-initiated)</see> Channel object examples.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channel")]
    public object? Channel { get; init; }

    /// <summary>
    /// A JSON Object representing the routing rules for the Interaction Channel. See <see href="https://www.twilio.com/docs/flex/developer/conversations/interactions-api/interactions#agent-initiated-outbound-interactions">Outbound SMS Example</see> for an example Routing object. The Interactions resource uses TaskRouter for all routing functionality.
    ///  All attributes in the Routing object on your Interaction request body are added “as is” to the task. For a list of known attributes consumed by the Flex UI and/or Flex Insights, see <see href="https://www.twilio.com/docs/flex/developer/conversations/interactions-api#task-attributes">Known Task Attributes</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("routing")]
    public object? Routing { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("interaction_context_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^HQ[0-9a-fA-F]{32}$")]
    public string? InteractionContextSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("webhook_ttid")]
    public string? WebhookTtid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
