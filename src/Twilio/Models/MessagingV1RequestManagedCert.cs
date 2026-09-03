using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record MessagingV1RequestManagedCert
{
    /// <summary>
    /// The unique string that we created to identify the Domain resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("domain_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^DN[0-9a-fA-F]{32}$")]
    public string? DomainSid { get; init; }

    /// <summary>
    /// Date that this Domain was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// Date that this Domain was registered to the Twilio platform to create a new Domain object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// Date that the private certificate associated with this domain expires. This is the expiration date of your existing cert.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_expires")]
    public DateTimeOffset? DateExpires { get; init; }

    /// <summary>
    /// Full url path for this domain.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("domain_name")]
    [Format(FormatKind.Uri)]
    public string? DomainName { get; init; }

    /// <summary>
    /// The unique string that we created to identify this Certificate resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("certificate_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CW[0-9a-fA-F]{32}$")]
    public string? CertificateSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// A boolean flag indicating if the certificate is managed by Twilio.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("managed")]
    public bool? Managed { get; init; }

    /// <summary>
    /// A boolean flag indicating if a managed certificate needs to be fulfilled by Twilio.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("requesting")]
    public bool? Requesting { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
