using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Strongly-typed Twilio configuration, bound from the <c>Twilio:</c> section. The credential
/// values are never hard-coded — they come from configuration (user-secrets / environment). The
/// auth token is a secret and is never logged or returned by an endpoint.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    [Required(AllowEmptyStrings = false)]
    public string AccountSid { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string AuthToken { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string FromNumber { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set, it is used verbatim as
    /// the base URL for every messaging-API call. It does not govern other Twilio hosts (e.g. Lookup).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How far ahead the delivery follow-up is queued with the provider. Defaults to 3 days.</summary>
    public TimeSpan FollowUpDelay { get; set; } = TimeSpan.FromDays(3);
}
