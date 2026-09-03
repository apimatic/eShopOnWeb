using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record RecordingAddOnResultPayloadData
{
    /// <summary>
    /// The URL to redirect to to get the data returned by the AddOn that was previously stored.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("redirect_to")]
    [Format(FormatKind.Uri)]
    public string? RedirectTo { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
