using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record ApiV2010AccountTranscription
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Transcription resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The API version used to create the transcription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; init; }

    /// <summary>
    /// The date and time in GMT that the resource was created specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public string? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT that the resource was last updated specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public string? DateUpdated { get; init; }

    /// <summary>
    /// The duration of the transcribed audio in seconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    /// <summary>
    /// The charge for the transcript in the currency associated with the account. This value is populated after the transcript is complete so it may not be available immediately.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price")]
    public double? Price { get; init; }

    /// <summary>
    /// The currency in which <c>price</c> is measured, in <see href="https://www.iso.org/iso/home/standards/currency_codes.htm">ISO 4127</see> format (e.g. <c>usd</c>, <c>eur</c>, <c>jpy</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_unit")]
    public string? PriceUnit { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/voice/api/recording">Recording</see> from which the transcription was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("recording_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RE[0-9a-fA-F]{32}$")]
    public string? RecordingSid { get; init; }

    /// <summary>
    /// The unique string that that we created to identify the Transcription resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^TR[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The status of the transcription. Can be: <c>in-progress</c>, <c>completed</c>, <c>failed</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public TranscriptionEnumStatus? Status { get; init; }

    /// <summary>
    /// The text content of the transcription.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transcription_text")]
    public string? TranscriptionText { get; init; }

    /// <summary>
    /// The transcription type. Can only be: <c>fast</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// The URI of the resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
