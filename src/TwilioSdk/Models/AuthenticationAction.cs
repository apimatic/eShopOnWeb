using System.Text.Json.Serialization;

namespace TwilioSdk.Models;

public record AuthenticationAction
{
    [JsonPropertyName("type")]
    public string Type { get; } = "COPY_CODE";

    [JsonPropertyName("copy_code_text")]
    public required string CopyCodeText { get; init; }
}
