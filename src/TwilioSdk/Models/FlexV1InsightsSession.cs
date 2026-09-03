using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record FlexV1InsightsSession
{
    /// <summary>
    /// Unique ID to identify the user's workspace
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("workspace_id")]
    public string? WorkspaceId { get; init; }

    /// <summary>
    /// The session expiry date and time, given in ISO 8601 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("session_expiry")]
    public string? SessionExpiry { get; init; }

    /// <summary>
    /// The unique ID for the session
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    /// <summary>
    /// The base URL to fetch reports and dashboards
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("base_url")]
    public string? BaseUrl { get; init; }

    /// <summary>
    /// The URL of this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
