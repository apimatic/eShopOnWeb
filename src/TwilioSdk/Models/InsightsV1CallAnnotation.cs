using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record InsightsV1CallAnnotation
{
    /// <summary>
    /// The unique SID identifier of the Call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CA[0-9a-fA-F]{32}$")]
    public string? CallSid { get; init; }

    /// <summary>
    /// The unique SID identifier of the Account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answered_by")]
    public AnnotationEnumAnsweredBy? AnsweredBy { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("connectivity_issue")]
    public AnnotationEnumConnectivityIssue? ConnectivityIssue { get; init; }

    /// <summary>
    /// Specifies if the call had any subjective quality issues. Possible values are one or more of <c>no_quality_issue</c>, <c>low_volume</c>, <c>choppy_robotic</c>, <c>echo</c>, <c>dtmf</c>, <c>latency</c>, <c>owa</c>, or <c>static_noise</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("quality_issues")]
    public IReadOnlyList<string?>? QualityIssues { get; init; }

    /// <summary>
    /// Specifies if the call was a spam call. Use this to provide feedback on whether calls placed from your account were marked as spam, or if inbound calls received by your account were unwanted spam. Is of type Boolean: true, false. Use true if the call was a spam call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("spam")]
    public bool? Spam { get; init; }

    /// <summary>
    /// Specifies the Call Score, if available. This is of type integer. Use a range of 1-5 to indicate the call experience score, with the following mapping as a reference for rating the call [5: Excellent, 4: Good, 3 : Fair, 2 : Poor, 1: Bad].
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_score")]
    public int? CallScore { get; init; }

    /// <summary>
    /// Specifies any comments pertaining to the call. Twilio does not treat this field as PII, so no PII should be included in comments.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>
    /// Incident or support ticket associated with this call. The <c>incident</c> property is of type string with a maximum character limit of 100. Twilio does not treat this field as PII, so no PII should be included in <c>incident</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("incident")]
    public string? Incident { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
