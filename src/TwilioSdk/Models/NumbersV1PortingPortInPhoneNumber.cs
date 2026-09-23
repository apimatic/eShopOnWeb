using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record NumbersV1PortingPortInPhoneNumber
{
    /// <summary>
    /// The unique identifier for the port in request that this phone number is associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_in_request_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KW[0-9a-fA-F]{32}$")]
    public string? PortInRequestSid { get; init; }

    /// <summary>
    /// The unique identifier for this phone number associated with this port in request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_number_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PU[0-9a-fA-F]{32}$")]
    public string? PhoneNumberSid { get; init; }

    /// <summary>
    /// URL reference for this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// Account Sid or subaccount where the phone number(s) will be Ported.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The number type of the phone number. This can be: toll-free, local, mobile or unknown. This field may be null if the number is not portable or if the portability for a number has not yet been evaluated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_number_type")]
    public string? PhoneNumberType { get; init; }

    /// <summary>
    /// The timestamp for when this port in phone number was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The ISO country code that this number is associated with. This field may be null if the number is not portable or if the portability for a number has not yet been evaluated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("country")]
    public string? Country { get; init; }

    /// <summary>
    /// Indicates if the phone number is missing required fields such as a PIN or account number. This field may be null if the number is not portable or if the portability for a number has not yet been evaluated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("missing_required_fields")]
    public bool? MissingRequiredFields { get; init; }

    /// <summary>
    /// Timestamp indicating when the Port In Phone Number resource was last modified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_updated")]
    public DateTimeOffset? LastUpdated { get; init; }

    /// <summary>
    /// Phone number to be ported. This will be in the E164 Format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// If the number is portable by Twilio or not. This field may be null if the number portability has not yet been evaluated. If a number is not portable reference the <c>not_portability_reason_code</c> and <c>not_portability_reason</c> fields for more details
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("portable")]
    public bool? Portable { get; init; }

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
    /// The status of the port in phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_in_phone_number_status")]
    public string? PortInPhoneNumberStatus { get; init; }

    /// <summary>
    /// The pin required by the losing carrier to do the port out.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_out_pin")]
    public int? PortOutPin { get; init; }

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
    public int? RejectionReasonCode { get; init; }

    /// <summary>
    /// The timestamp the phone number will be ported. This will only be set once a port date has been confirmed. Not all carriers can guarantee a specific time on the port date. Twilio will try its best to get the port completed by this time on the port date. Please subscribe to webhooks for confirmation on when a port has actually been completed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_date")]
    public DateTimeOffset? PortDate { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
