using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record NumbersV2AuthorizationDocumentDependentHostedNumberOrder
{
    /// <summary>
    /// A 34 character string that uniquely identifies this Authorization Document
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^HR[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

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
    /// The unique SID identifier of the Account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// A 34 character string that uniquely identifies the IncomingPhoneNumber resource created by this HostedNumberOrder.
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
    /// A 34 character string that uniquely identifies the LOA document associated with this HostedNumberOrder.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("signing_document_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PX[0-9a-fA-F]{32}$")]
    public string? SigningDocumentSid { get; init; }

    /// <summary>
    /// An E164 formatted phone number hosted by this HostedNumberOrder.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// A mapping of capabilities this hosted phone number will have enabled on Twilio's platform.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("capabilities")]
    public Capabilities? Capabilities { get; init; }

    /// <summary>
    /// A human readable description of this resource, up to 128 characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// Status of an instance resource. It can hold one of the values: 1. opened 2. signing, 3. signed LOA, 4. canceled, 5. failed. See the section entitled <see href="https://www.twilio.com/docs/phone-numbers/hosted-numbers/hosted-numbers-api/authorization-document-resource#status-values">Status Values</see> for more information on each of these statuses.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public DependentHostedNumberOrderEnumStatus? Status { get; init; }

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
    /// Email recipients who will be informed when an Authorization Document has been sent and signed
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cc_emails")]
    public IReadOnlyList<string?>? CcEmails { get; init; }

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

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
