using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record NumbersV1PortingPortIn
{
    /// <summary>
    /// The SID of the Port In request. This is a unique identifier of the port in request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_in_request_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KW[0-9a-fA-F]{32}$")]
    public string? PortInRequestSid { get; init; }

    /// <summary>
    /// The URL of this Port In request
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// Account Sid or subaccount where the phone number(s) will be Ported
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// Additional emails to send a copy of the signed LOA to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("notification_emails")]
    public IReadOnlyList<string?>? NotificationEmails { get; init; }

    /// <summary>
    /// Target date to port the number. We cannot guarantee that this date will be honored by the other carriers, please work with Ops to get a confirmation of the firm order commitment (FOC) date. Expected format is ISO Local Date, example: ‘2011-12-03`. This date must be at least 7 days in the future for US ports and 10 days in the future for Japanese ports. If a start and end range is provided, the date will be converted to its UTC equivalent with the ranges as reference and stored in UTC. We can't guarantee the exact date and time, as this depends on the losing carrier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("target_port_in_date")]
    public DateTimeOffset? TargetPortInDate { get; init; }

    /// <summary>
    /// The earliest time that the port should occur on the target port in date. Expected format is ISO Offset Time, example: ‘10:15:00-08:00'. We can't guarantee the exact date and time, as this depends on the losing carrier. The time will be stored and returned as UTC standard timezone.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("target_port_in_time_range_start")]
    public string? TargetPortInTimeRangeStart { get; init; }

    /// <summary>
    /// The latest time that the port should occur on the target port in date. Expected format is ISO Offset Time, example: ‘10:15:00-08:00'. We can't guarantee the exact date and time, as this depends on the losing carrier. The time will be stored and returned as UTC standard timezone.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("target_port_in_time_range_end")]
    public string? TargetPortInTimeRangeEnd { get; init; }

    /// <summary>
    /// The status of the port in request. The possible values are: In progress, Completed, Expired, In review, Waiting for Signature, Action Required, and Canceled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_in_request_status")]
    public string? PortInRequestStatus { get; init; }

    /// <summary>
    /// If the order is cancelled this field will provide further context on the cause of the cancellation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("order_cancellation_reason")]
    public string? OrderCancellationReason { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("losing_carrier_information")]
    public LosingCarrierInformation? LosingCarrierInformation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_numbers")]
    public IReadOnlyList<PhoneNumberResult?>? PhoneNumbers { get; init; }

    /// <summary>
    /// The bundle sid is an optional identifier to reference a group of regulatory documents for a port request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bundle_sid")]
    public string? BundleSid { get; init; }

    /// <summary>
    /// A field only required for Japan port in requests. It is a unique identifier for the donor carrier service the line is being ported from.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("portability_advance_carrier")]
    public string? PortabilityAdvanceCarrier { get; init; }

    /// <summary>
    /// Japan specific field, indicates the number of phone numbers to automatically approve for cancellation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("auto_cancel_approval_numbers")]
    public string? AutoCancelApprovalNumbers { get; init; }

    /// <summary>
    /// List of document SIDs for all phone numbers included in the port in request. At least one document SID referring to a document of the type Utility Bill is required.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("documents")]
    public IReadOnlyList<string?>? Documents { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// Unique ID of the request's support ticket
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("support_ticket_id")]
    public int? SupportTicketId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("signature_request_url")]
    [Format(FormatKind.Uri)]
    public string? SignatureRequestUrl { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
