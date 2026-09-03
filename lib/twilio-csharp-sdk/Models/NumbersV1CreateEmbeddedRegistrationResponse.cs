using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record NumbersV1CreateEmbeddedRegistrationResponse
{
    /// <summary>
    /// Registration identifier (BU-prefixed).
    /// </summary>
    [JsonPropertyName("id")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public required string Id { get; init; }

    /// <summary>
    /// The regulation ID for this registration.
    /// </summary>
    [JsonPropertyName("regulationId")]
    public required string RegulationId { get; init; }

    /// <summary>
    /// The regulation version.
    /// </summary>
    [JsonPropertyName("regulationVersion")]
    public required int RegulationVersion { get; init; }

    /// <summary>
    /// The friendly name provided in the request.
    /// </summary>
    [JsonPropertyName("friendlyName")]
    public required string FriendlyName { get; init; }

    /// <summary>
    /// Registration status. Always DRAFT on creation.
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Email address for status notifications.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusNotificationEmail")]
    [Format(FormatKind.Email)]
    public string? StatusNotificationEmail { get; init; }

    /// <summary>
    /// Callback URL for status webhooks.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusCallbackUrl")]
    public string? StatusCallbackUrl { get; init; }

    /// <summary>
    /// Additional comments.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("comments")]
    public string? Comments { get; init; }

    [JsonPropertyName("embeddedSession")]
    public required NumbersV1EmbeddedSession EmbeddedSession { get; init; }

    /// <summary>
    /// Registration data echoed from the request.
    /// </summary>
    [JsonPropertyName("data")]
    public required object Data { get; init; }

    /// <summary>
    /// Timestamp of creation.
    /// </summary>
    [JsonPropertyName("dateCreated")]
    public required DateTimeOffset DateCreated { get; init; }

    /// <summary>
    /// Timestamp of last update.
    /// </summary>
    [JsonPropertyName("dateUpdated")]
    public required DateTimeOffset DateUpdated { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
