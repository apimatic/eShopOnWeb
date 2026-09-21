using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record MessagingV1BrandRegistrationsBrandVetting
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the vetting record.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The unique string to identify Brand Registration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BN[0-9a-fA-F]{32}$")]
    public string? BrandSid { get; init; }

    /// <summary>
    /// The Twilio SID of the third-party vetting record.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("brand_vetting_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^VT[0-9a-fA-F]{32}$")]
    public string? BrandVettingSid { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was last updated specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was created specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The unique identifier of the vetting from the third-party provider.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vetting_id")]
    public string? VettingId { get; init; }

    /// <summary>
    /// The type of vetting that has been conducted. One of “STANDARD” (Aegis) or “POLITICAL” (Campaign Verify).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vetting_class")]
    public string? VettingClass { get; init; }

    /// <summary>
    /// The status of the import vetting attempt. One of “PENDING,” “SUCCESS,” or “FAILED”.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vetting_status")]
    public string? VettingStatus { get; init; }

    /// <summary>
    /// The third-party provider that has conducted the vetting. One of “CampaignVerify” (Campaign Verify tokens) or “AEGIS” (Secondary Vetting).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vetting_provider")]
    public BrandVettingEnumVettingProvider? VettingProvider { get; init; }

    /// <summary>
    /// The absolute URL of the Brand Vetting resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
