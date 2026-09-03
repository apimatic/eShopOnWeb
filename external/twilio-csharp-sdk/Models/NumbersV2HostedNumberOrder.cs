using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record NumbersV2HostedNumberOrder
{
    /// <summary>
    /// A 34 character string that uniquely identifies this HostedNumberOrder.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^HR[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies the account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies the <see href="https://www.twilio.com/docs/phone-numbers/api/incomingphonenumber-resource">IncomingPhoneNumber</see> resource that represents the phone number being hosted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("incoming_phone_number_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PN[0-9a-fA-F]{32}$")]
    public string? IncomingPhoneNumberSid { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies the Address resource that represents the address of the owner of this phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AD[0-9a-fA-F]{32}$")]
    public string? AddressSid { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies the <see href="https://www.twilio.com/docs/phone-numbers/hosted-numbers/hosted-numbers-api/authorization-document-resource">Authorization Document</see> the user needs to sign.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("signing_document_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PX[0-9a-fA-F]{32}$")]
    public string? SigningDocumentSid { get; init; }

    /// <summary>
    /// Phone number to be hosted. This must be in <see href="https://en.wikipedia.org/wiki/E.164">E.164</see> format, e.g., +16175551212
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// Set of booleans describing the capabilities hosted on Twilio's platform. SMS is currently only supported.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("capabilities")]
    public Capabilities? Capabilities { get; init; }

    /// <summary>
    /// A 128 character string that is a human-readable text that describes this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// Status of this resource. It can hold one of the values: 1. Twilio Processing 2. Received, 3. Pending LOA, 4. Carrier Processing, 5. Completed, 6. Action Required, 7. Failed. See the <see href="https://www.twilio.com/docs/phone-numbers/hosted-numbers/hosted-numbers-api/hosted-number-order-resource#status-values">HostedNumberOrders Status Values</see> section for more information on each of these statuses.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public DependentOrderEnumStatus? Status { get; init; }

    /// <summary>
    /// A message that explains why a hosted_number_order went to status "action-required"
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; init; }

    /// <summary>
    /// The date this resource was created, given as <see href="http://www.ietf.org/rfc/rfc2822.txt">GMT RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date that this resource was updated, given as <see href="http://www.ietf.org/rfc/rfc2822.txt">GMT RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// Email of the owner of this phone number that is being hosted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>
    /// A list of emails that LOA document for this HostedNumberOrder will be carbon copied to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cc_emails")]
    public IReadOnlyList<string?>? CcEmails { get; init; }

    /// <summary>
    /// The URL of this HostedNumberOrder.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The title of the person authorized to sign the Authorization Document for this phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("contact_title")]
    public string? ContactTitle { get; init; }

    /// <summary>
    /// The contact phone number of the person authorized to sign the Authorization Document.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("contact_phone_number")]
    public string? ContactPhoneNumber { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies the bulk hosting request associated with this HostedNumberOrder.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bulk_hosting_request_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BH[0-9a-fA-F]{32}$")]
    public string? BulkHostingRequestSid { get; init; }

    /// <summary>
    /// The next step you need to take to complete the hosted number order and request it successfully.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_step")]
    public string? NextStep { get; init; }

    /// <summary>
    /// The number of attempts made to verify ownership via a call for the hosted phone number.
    /// </summary>
    [JsonPropertyName("verification_attempts")]
    public int? VerificationAttempts { get; init; } = 0;

    /// <summary>
    /// The Call SIDs that identify the calls placed to verify ownership.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verification_call_sids")]
    public IReadOnlyList<string?>? VerificationCallSids { get; init; }

    /// <summary>
    /// The number of seconds to wait before initiating the ownership verification call. Can be a value between 0 and 60, inclusive.
    /// </summary>
    [JsonPropertyName("verification_call_delay")]
    public int? VerificationCallDelay { get; init; } = 0;

    /// <summary>
    /// The numerical extension to dial when making the ownership verification call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verification_call_extension")]
    public string? VerificationCallExtension { get; init; }

    /// <summary>
    /// The digits the user must pass in the ownership verification call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verification_code")]
    public string? VerificationCode { get; init; }

    /// <summary>
    /// The method used to verify ownership of the number to be hosted. Can be: <c>phone-call</c> or <c>phone-bill</c> and the default is <c>phone-call</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verification_type")]
    public HostedNumberOrderEnumVerificationType1? VerificationType { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
