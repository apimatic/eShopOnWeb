using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

/// <summary>
/// Status of a long-running operation.
/// </summary>
public record ConversationsV2OperationStatus
{
    /// <summary>
    /// Unique identifier for the long-running operation.
    /// </summary>
    [JsonPropertyName("operationId")]
    public required string OperationId { get; init; }

    /// <summary>
    /// Current status of the operation.
    /// </summary>
    [JsonPropertyName("status")]
    public required Status21 Status { get; init; }

    /// <summary>
    /// Timestamp when the operation was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Timestamp when the operation completed. Only present for completed or failed operations.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// URL to poll for operation status.
    /// </summary>
    [JsonPropertyName("statusUrl")]
    [Format(FormatKind.Uri)]
    public required string StatusUrl { get; init; }

    /// <summary>
    /// Error details if the operation failed. Follows RFC 9457 Problem Details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error")]
    public Error? Error { get; init; }

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
