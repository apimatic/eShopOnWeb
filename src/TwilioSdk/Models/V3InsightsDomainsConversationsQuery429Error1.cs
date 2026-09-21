using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record V3InsightsDomainsConversationsQuery429Error1
{
    /// <summary>
    /// Twilio-specific error code
    /// </summary>
    [JsonPropertyName("code")]
    public required int Code { get; init; }

    /// <summary>
    /// A human readable error message
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// HTTP response status code
    /// </summary>
    [JsonPropertyName("httpStatusCode")]
    public required int HttpStatusCode { get; init; }

    /// <summary>
    /// Whether the error is a user error (true) or a system error (false)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("userError")]
    public bool? UserError { get; init; }

    /// <summary>
    /// A map of parameters related to the error, for example, a <c>params.twilioErrorCodeUrl</c> might hold a URL or link to additional information
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("params")]
    public IReadOnlyDictionary<string, string>? Params { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
