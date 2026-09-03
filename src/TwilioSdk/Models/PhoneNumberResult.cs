using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record PhoneNumberResult
{
    /// <summary>
    /// The not portability reason code description. This field may be null if the number is portable or if the portability for a number has not yet been evaluated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("not_portability_reason")]
    public string? NotPortabilityReason { get; init; }

    /// <summary>
    /// The not portability reason code. This field may be null if the number is portable or if the portability for a number has not yet been evaluated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("not_portability_reason_code")]
    public int? NotPortabilityReasonCode { get; init; }

    /// <summary>
    /// The number type of the phone number. This can be: toll-free, local, mobile or unknown. This field may be null if the number is not portable or if the portability for a number has not yet been evaluated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("number_type")]
    public string? NumberType { get; init; }

    /// <summary>
    /// Phone number to be ported. This will be in the E164 Format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// The timestamp the phone number will be ported. This will only be set once a port date has been confirmed. Not all carriers can guarantee a specific time on the port date. Twilio will try its best to get the port completed by this time on the port date. Please subscribe to webhooks for confirmation on when a port has actually been completed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_date")]
    public DateTimeOffset? PortDate { get; init; }

    /// <summary>
    /// The SID of the Phone number. This is a unique identifier of the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_in_phone_number_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PU[0-9a-fA-F]{32}$")]
    public string? PortInPhoneNumberSid { get; init; }

    /// <summary>
    /// The status of the port in phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_in_phone_number_status")]
    public string? PortInPhoneNumberStatus { get; init; }

    /// <summary>
    /// Whether the number is portable by Twilio or not. This field may be null if the number portability has not yet been evaluated. If a number is not portable reference the <c>not_portability_reason_code</c> and <c>not_portability_reason</c> fields for more details
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("portable")]
    public bool? Portable { get; init; }

    /// <summary>
    /// The description of the rejection reason provided by the losing carrier. This field may be null if the number has not been rejected by the losing carrier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rejection_reason")]
    public string? RejectionReason { get; init; }

    /// <summary>
    /// The code for the rejection reason provided by the losing carrier. This field may be null if the number has not been rejected by the losing carrier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rejection_reason_code")]
    public string? RejectionReasonCode { get; init; }

    /// <summary>
    /// Timestamp indicating when the Port In Phone Number resource was last modified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_last_time_updated_timestamp")]
    public string? StatusLastTimeUpdatedTimestamp { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("external_porting_vendor_phone_number_id")]
    public string? ExternalPortingVendorPhoneNumberId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
