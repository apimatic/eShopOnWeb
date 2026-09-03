using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record SyncV1ServiceSyncStreamStreamMessage
{
    /// <summary>
    /// The unique string that we created to identify the Stream Message resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^TZ[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// An arbitrary, schema-less object that contains the Stream Message body. Can be up to 4 KiB in length.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("data")]
    public object? Data { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
