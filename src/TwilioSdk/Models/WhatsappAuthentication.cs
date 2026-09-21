using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TwilioSdk.Models;

/// <summary>
/// whatsApp/authentication templates let companies deliver WA approved one-time-password button.
/// </summary>
public record WhatsappAuthentication
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("add_security_recommendation")]
    public bool? AddSecurityRecommendation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("code_expiration_minutes")]
    public double? CodeExpirationMinutes { get; init; }

    [JsonPropertyName("actions")]
    public required IReadOnlyList<AuthenticationAction> Actions { get; init; }
}
