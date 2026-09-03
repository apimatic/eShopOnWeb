using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record SenderIdValidationFailure
{
    /// <summary>
    /// Associated error code with validation failure
    /// </summary>
    [JsonPropertyName("error_code")]
    public required int ErrorCode { get; init; }

    /// <summary>
    /// Friendly description of error for validation failure
    /// </summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
