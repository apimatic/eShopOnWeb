using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ProxyV1ServiceSession
{
    /// <summary>
    /// The unique string that we created to identify the Session resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KC[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> the session is associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KS[0-9a-fA-F]{32}$")]
    public string? ServiceSid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Session resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date when the Session started.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_started")]
    public DateTimeOffset? DateStarted { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date when the Session ended.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_ended")]
    public DateTimeOffset? DateEnded { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date when the Session last had an interaction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_last_interaction")]
    public DateTimeOffset? DateLastInteraction { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date when the Session should expire. If this is value is present, it overrides the <c>ttl</c> value.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_expiry")]
    public DateTimeOffset? DateExpiry { get; init; }

    /// <summary>
    /// An application-defined string that uniquely identifies the resource. This value must be 191 characters or fewer in length and be unique. Supports UTF-8 characters. <b>This value should not have PII.</b>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unique_name")]
    public string? UniqueName { get; init; }

    /// <summary>
    /// The status of the Session. Can be: <c>open</c>, <c>in-progress</c>, <c>closed</c>, <c>failed</c>, or <c>unknown</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public SessionEnumStatus? Status { get; init; }

    /// <summary>
    /// The reason the Session ended.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("closed_reason")]
    public string? ClosedReason { get; init; }

    /// <summary>
    /// The time, in seconds, when the session will expire. The time is measured from the last Session create or the Session's last Interaction.
    /// </summary>
    [JsonPropertyName("ttl")]
    public int? Ttl { get; init; } = 0;

    /// <summary>
    /// The Mode of the Session. Can be: <c>message-only</c>, <c>voice-only</c>, or <c>voice-and-message</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mode")]
    public SessionEnumMode? Mode { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date and time in GMT when the resource was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date and time in GMT when the resource was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The absolute URL of the Session resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The URLs of resources related to the Session.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
