using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record SenderIdValidationResult
{
    /// <summary>
    /// ISO 3166-1 alpha-2 standard Country Code
    /// </summary>
    [JsonPropertyName("iso_country")]
    public required string IsoCountry { get; init; }

    /// <summary>
    /// Purpose for using Sender ID
    /// </summary>
    [JsonPropertyName("purpose")]
    public required SenderIdPurpose Purpose { get; init; }

    /// <summary>
    /// Sender ID string
    /// </summary>
    [JsonPropertyName("sender_id")]
    public required string SenderId { get; init; }

    /// <summary>
    /// List of failures during the validation
    /// </summary>
    [JsonPropertyName("failures")]
    public required IReadOnlyList<SenderIdValidationFailure> Failures { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
