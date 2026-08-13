using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Settings for the SMS provider integration, bound from the <c>Twilio:</c> configuration section.
/// Deliberately does <b>not</b> carry the auth token: the token is a secret read straight into the
/// SDK client at construction time and is never placed on an injectable/loggable settings object.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>The account SID (also the basic-auth username).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>This application's own configured sending number (E.164). Immediate messages are sent from here and reconciliation counts only messages from it.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required by the provider for scheduled (future-dated) messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the messaging API base address. When set it is used verbatim for every messaging-API call.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>How far ahead the "how did delivery go?" follow-up is queued with the provider. Provider allows up to 7 days.</summary>
    public TimeSpan FollowUpDelay { get; set; } = TimeSpan.FromDays(3);
}
