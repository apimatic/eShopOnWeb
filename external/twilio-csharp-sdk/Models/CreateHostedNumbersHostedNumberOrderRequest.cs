using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record CreateHostedNumbersHostedNumberOrderRequest
{
    /// <summary>
    /// The number to host in <see href="https://en.wikipedia.org/wiki/E.164">+E.164</see> format
    /// </summary>
    [JsonPropertyName("phoneNumber")]
    public required string PhoneNumber { get; init; }

    /// <summary>
    /// Used to specify that the SMS capability will be hosted on Twilio's platform.
    /// </summary>
    [JsonPropertyName("smsCapability")]
    public required bool SmsCapability { get; init; }

    /// <summary>
    /// This defaults to the AccountSid of the authorization the user is using. This can be provided to specify a subaccount to add the HostedNumberOrder to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("accountSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// A 64 character string that is a human readable text that describes this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendlyName")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// Optional. Provides a unique and addressable name to be assigned to this HostedNumberOrder, assigned by the developer, to be optionally used in addition to SID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uniqueName")]
    public string? UniqueName { get; init; }

    /// <summary>
    /// Optional. A list of emails that the LOA document for this HostedNumberOrder will be carbon copied to.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ccEmails")]
    public IReadOnlyList<string>? CcEmails { get; init; }

    /// <summary>
    /// The URL that Twilio should request when somebody sends an SMS to the phone number. This will be copied onto the IncomingPhoneNumber resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("smsUrl")]
    [Format(FormatKind.Uri)]
    public string? SmsUrl { get; init; }

    /// <summary>
    /// The HTTP method that should be used to request the SmsUrl. Must be either <c>GET</c> or <c>POST</c>.  This will be copied onto the IncomingPhoneNumber resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("smsMethod")]
    public AmdStatusCallbackMethod? SmsMethod { get; init; }

    /// <summary>
    /// A URL that Twilio will request if an error occurs requesting or executing the TwiML defined by SmsUrl. This will be copied onto the IncomingPhoneNumber resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("smsFallbackUrl")]
    [Format(FormatKind.Uri)]
    public string? SmsFallbackUrl { get; init; }

    /// <summary>
    /// The HTTP method that should be used to request the SmsFallbackUrl. Must be either <c>GET</c> or <c>POST</c>. This will be copied onto the IncomingPhoneNumber resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("smsFallbackMethod")]
    public AmdStatusCallbackMethod? SmsFallbackMethod { get; init; }

    /// <summary>
    /// Optional. The Status Callback URL attached to the IncomingPhoneNumber resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusCallbackUrl")]
    [Format(FormatKind.Uri)]
    public string? StatusCallbackUrl { get; init; }

    /// <summary>
    /// Optional. The Status Callback Method attached to the IncomingPhoneNumber resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("statusCallbackMethod")]
    public AmdStatusCallbackMethod? StatusCallbackMethod { get; init; }

    /// <summary>
    /// Optional. The 34 character sid of the application Twilio should use to handle SMS messages sent to this number. If a <c>SmsApplicationSid</c> is present, Twilio will ignore all of the SMS urls above and use those set on the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("smsApplicationSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AP[0-9a-fA-F]{32}$")]
    public string? SmsApplicationSid { get; init; }

    /// <summary>
    /// Optional. A 34 character string that uniquely identifies the Address resource that represents the address of the owner of this phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("addressSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AD[0-9a-fA-F]{32}$")]
    public string? AddressSid { get; init; }

    /// <summary>
    /// Optional. Email of the owner of this phone number that is being hosted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verificationType")]
    public DependentOrderEnumVerificationType? VerificationType { get; init; }

    /// <summary>
    /// Optional. The unique sid identifier of the Identity Document that represents the document for verifying ownership of the number to be hosted. Required when VerificationType is phone-bill.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verificationDocumentSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RI[0-9a-fA-F]{32}$")]
    public string? VerificationDocumentSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
