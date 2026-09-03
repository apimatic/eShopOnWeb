using System.Text.Json.Serialization;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record AuthenticationAction
{
    [JsonPropertyName("type")]
    public required AuthenticationActionType Type { get; init; }

    [JsonPropertyName("copy_code_text")]
    public required string CopyCodeText { get; init; }
}
