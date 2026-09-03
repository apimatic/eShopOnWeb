using System.Collections.Generic;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record MessagingV1Usecase
{
    /// <summary>
    /// Human readable use case details (usecase, description and purpose) of Messaging Service Use Cases.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usecases")]
    public IReadOnlyList<object?>? Usecases { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
