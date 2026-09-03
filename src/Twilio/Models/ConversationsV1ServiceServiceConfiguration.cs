using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record ConversationsV1ServiceServiceConfiguration
{
    /// <summary>
    /// The unique string that we created to identify the Service configuration resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("chat_service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IS[0-9a-fA-F]{32}$")]
    public string? ChatServiceSid { get; init; }

    /// <summary>
    /// The conversation-level role assigned to a conversation creator when they join a new conversation. See <see href="https://www.twilio.com/docs/conversations/api/role-resource">Conversation Role</see> for more info about roles.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_conversation_creator_role_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RL[0-9a-fA-F]{32}$")]
    public string? DefaultConversationCreatorRoleSid { get; init; }

    /// <summary>
    /// The conversation-level role assigned to users when they are added to a conversation. See <see href="https://www.twilio.com/docs/conversations/api/role-resource">Conversation Role</see> for more info about roles.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_conversation_role_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RL[0-9a-fA-F]{32}$")]
    public string? DefaultConversationRoleSid { get; init; }

    /// <summary>
    /// The service-level role assigned to users when they are added to the service. See <see href="https://www.twilio.com/docs/conversations/api/role-resource">Conversation Role</see> for more info about roles.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_chat_service_role_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RL[0-9a-fA-F]{32}$")]
    public string? DefaultChatServiceRoleSid { get; init; }

    /// <summary>
    /// An absolute API resource URL for this service configuration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// Contains an absolute API resource URL to access the push notifications configuration of this service.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    /// <summary>
    /// Whether the <see href="https://www.twilio.com/docs/conversations/reachability">Reachability Indicator</see> is enabled for this Conversations Service. The default is <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reachability_enabled")]
    public bool? ReachabilityEnabled { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
