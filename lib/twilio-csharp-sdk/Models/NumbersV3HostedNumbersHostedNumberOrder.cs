using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record NumbersV3HostedNumbersHostedNumberOrder
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
    [JsonPropertyName("accountSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies the <see href="https://www.twilio.com/docs/phone-numbers/api/incomingphonenumber-resource">IncomingPhoneNumber</see> resource that represents the phone number being hosted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("incomingPhoneNumberSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PN[0-9a-fA-F]{32}$")]
    public string? IncomingPhoneNumberSid { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies the Address resource that represents the address of the owner of this phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("addressSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AD[0-9a-fA-F]{32}$")]
    public string? AddressSid { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies the <see href="https://www.twilio.com/docs/phone-numbers/hosted-numbers/hosted-numbers-api/authorization-document-resource">Authorization Document</see> the user needs to sign.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("signingDocumentSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PX[0-9a-fA-F]{32}$")]
    public string? SigningDocumentSid { get; init; }

    /// <summary>
    /// Phone number to be hosted. This must be in <see href="https://en.wikipedia.org/wiki/E.164">E.164</see> format, e.g., +16175551212
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// Set of booleans describing the capabilities hosted on Twilio's platform. SMS is currently only supported.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("capabilities")]
    public Capabilities2? Capabilities { get; init; }

    /// <summary>
    /// A 64 character string that is a human-readable text that describes this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendlyName")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// Provides a unique and addressable name to be assigned to this HostedNumberOrder, assigned by the developer, to be optionally used in addition to SID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uniqueName")]
    public string? UniqueName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public DependentOrderEnumStatus? Status { get; init; }

    /// <summary>
    /// A message that explains why a hosted_number_order went to status "action-required"
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }

    /// <summary>
    /// The date this resource was created, given as <see href="http://www.ietf.org/rfc/rfc2822.txt">GMT RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dateCreated")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date that this resource was updated, given as <see href="http://www.ietf.org/rfc/rfc2822.txt">GMT RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dateUpdated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The number of attempts made to verify ownership of the phone number that is being hosted.
    /// </summary>
    [JsonPropertyName("verificationAttempts")]
    public int? VerificationAttempts { get; init; } = 0;

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
    [JsonPropertyName("ccEmails")]
    public IReadOnlyList<string?>? CcEmails { get; init; }

    /// <summary>
    /// The URL of this HostedNumberOrder.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verificationType")]
    public DependentOrderEnumVerificationType? VerificationType { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies the Identity Document resource that represents the document for verifying ownership of the number to be hosted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verificationDocumentSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RI[0-9a-fA-F]{32}$")]
    public string? VerificationDocumentSid { get; init; }

    /// <summary>
    /// A numerical extension to be used when making the ownership verification call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("extension")]
    public string? Extension { get; init; }

    /// <summary>
    /// A value between 0-30 specifying the number of seconds to delay initiating the ownership verification call.
    /// </summary>
    [JsonPropertyName("callDelay")]
    public int? CallDelay { get; init; } = 0;

    /// <summary>
    /// A verification code provided in the response for a user to enter when they pick up the phone call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verificationCode")]
    public string? VerificationCode { get; init; }

    /// <summary>
    /// A list of 34 character strings that are unique identifiers for the calls placed as part of ownership verification.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verificationCallSids")]
    public IReadOnlyList<string?>? VerificationCallSids { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
