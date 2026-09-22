using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record MessagingV1DomainConfig
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
    /// The unique string that we created to identify the Domain config (prefix ZK).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("config_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^ZK[0-9a-fA-F]{32}$")]
    public string? ConfigSid { get; init; }

    /// <summary>
    /// Any requests we receive to this domain that do not match an existing shortened message will be redirected to the fallback url. These will likely be either expired messages, random misdirected traffic, or intentional scraping.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fallback_url")]
    [Format(FormatKind.Uri)]
    public string? FallbackUrl { get; init; }

    /// <summary>
    /// URL to receive click events to your webhook whenever the recipients click on the shortened links.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("callback_url")]
    [Format(FormatKind.Uri)]
    public string? CallbackUrl { get; init; }

    /// <summary>
    /// Boolean field to set customer delivery preference when there is a failure in linkShortening service
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("continue_on_failure")]
    public bool? ContinueOnFailure { get; init; }

    /// <summary>
    /// Date this Domain Config was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// Date that this Domain Config was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// Customer's choice to send links with/without "https://" attached to shortened url. If true, messages will not be sent with https:// at the beginning of the url. If false, messages will be sent with https:// at the beginning of the url. False is the default behavior if it is not specified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("disable_https")]
    public bool? DisableHttps { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
