using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record FlexV2WebChannel
{
    /// <summary>
    /// The unique string representing the <see href="https://www.twilio.com/docs/conversations/api/conversation-resource">Conversation resource</see> created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conversation_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CH[0-9a-fA-F]{32}$")]
    public string? ConversationSid { get; init; }

    /// <summary>
    /// The unique string representing the User created and should be authorized to participate in the Conversation. For more details, see <see href="https://www.twilio.com/docs/conversations/identity">User Identity &amp; Access Tokens</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("identity")]
    public string? Identity { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
