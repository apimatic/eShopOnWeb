using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record VerifyV2ServiceVerificationCheck
{
    /// <summary>
    /// The unique string that we created to identify the VerificationCheck resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^VE[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/verify/api/service">Service</see> the resource is associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^VA[0-9a-fA-F]{32}$")]
    public string? ServiceSid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the VerificationCheck resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The phone number or <see href="https://www.twilio.com/docs/verify/email">email</see> being verified. Phone numbers must be in <see href="https://www.twilio.com/docs/glossary/what-e164">E.164 format</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("to")]
    public string? To { get; init; }

    /// <summary>
    /// The verification method to use. One of: <see href="https://www.twilio.com/docs/verify/email"><c>email</c></see>, <c>sms</c>, <c>whatsapp</c>, <c>call</c>, or <c>sna</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("channel")]
    public VerificationCheckEnumChannel? Channel { get; init; }

    /// <summary>
    /// The status of the verification. Can be: <c>pending</c>, <c>approved</c>, <c>canceled</c>, <c>max_attempts_reached</c>, <c>deleted</c>, <c>failed</c> or <c>expired</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>
    /// Use "status" instead. Legacy property indicating whether the verification was successful.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("valid")]
    public bool? Valid { get; init; }

    /// <summary>
    /// The amount of the associated PSD2 compliant transaction. Requires the PSD2 Service flag enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("amount")]
    public string? Amount { get; init; }

    /// <summary>
    /// The payee of the associated PSD2 compliant transaction. Requires the PSD2 Service flag enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payee")]
    public string? Payee { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date and time in GMT when the Verification Check resource was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date and time in GMT when the Verification Check resource was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// List of error codes as a result of attempting a verification using the <c>sna</c> channel. The error codes are chronologically ordered, from the first attempt to the latest attempt. This will be an empty list if no errors occured or <c>null</c> if the last channel used wasn't <c>sna</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sna_attempts_error_codes")]
    public IReadOnlyList<object?>? SnaAttemptsErrorCodes { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
