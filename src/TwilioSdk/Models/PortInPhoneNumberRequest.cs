using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record PortInPhoneNumberRequest
{
    /// <summary>
    /// The SID of the Port In Phone Number resource that is being updated.
    /// </summary>
    [JsonPropertyName("port_in_phone_number_sid")]
    [MinLength(34)]
    [RegularExpression("^PU[0-9a-fA-F]{32}$")]
    public required string PortInPhoneNumberSid { get; init; }

    /// <summary>
    /// The timestamp the phone number will be ported. This will only be set once a port date has been confirmed. Not all carriers can guarantee a specific time on the port date. Twilio will try its best to get the port completed by this time on the port date.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_date")]
    public DateTimeOffset? PortDate { get; init; }

    /// <summary>
    /// The description of the rejection reason provided by the losing carrier. This field may be null if the number has not been rejected by the losing carrier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rejection_reason")]
    public RejectionReason? RejectionReason { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
