using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record MessagingV1ServiceDestinationAlphaSender
{
    /// <summary>
    /// The unique string that we created to identify the AlphaSender resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AI[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the AlphaSender resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/chat/rest/service-resource">Service</see> the resource is associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^MG[0-9a-fA-F]{32}$")]
    public string? ServiceSid { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was created specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was last updated specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The Alphanumeric Sender ID string.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("alpha_sender")]
    public string? AlphaSender { get; init; }

    /// <summary>
    /// An array of values that describe whether the number can receive calls or messages. Can be: <c>SMS</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string?>? Capabilities { get; init; }

    /// <summary>
    /// The absolute URL of the AlphaSender resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The Two Character ISO Country Code the Alphanumeric Sender ID will be used for. For Default Alpha Senders that work across countries, this value will be an empty string
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("iso_country_code")]
    public string? IsoCountryCode { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
