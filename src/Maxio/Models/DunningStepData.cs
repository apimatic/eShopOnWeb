using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record DunningStepData
{
    [JsonPropertyName("day_threshold")]
    public required int DayThreshold { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("email_body")]
    public string? EmailBody { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("email_subject")]
    public string? EmailSubject { get; init; }

    [JsonPropertyName("send_email")]
    public required bool SendEmail { get; init; }

    [JsonPropertyName("send_bcc_email")]
    public required bool SendBccEmail { get; init; }

    [JsonPropertyName("send_sms")]
    public required bool SendSms { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms_body")]
    public string? SmsBody { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
