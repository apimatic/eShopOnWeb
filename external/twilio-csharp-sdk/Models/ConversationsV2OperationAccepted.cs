using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

/// <summary>
/// Slim response for an accepted long-running operation.
/// </summary>
public record ConversationsV2OperationAccepted
{
    /// <summary>
    /// URL to poll for operation status.
    /// </summary>
    [JsonPropertyName("statusUrl")]
    [Format(FormatKind.Uri)]
    public required string StatusUrl { get; init; }

    /// <summary>
    /// Named resource identifiers associated with this operation. Keys depend on the operation type:
    /// - config-create, config-update, config-delete: configurationId
    /// - conversation-delete: conversationId
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("related")]
    public IReadOnlyDictionary<string, string?>? Related { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
