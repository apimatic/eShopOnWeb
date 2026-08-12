namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

/// <summary>
/// Twilio configuration, bound from the <c>Twilio:</c> section. Every value comes from configuration; none is
/// hard-coded, so the same build runs against a different Twilio account. The <see cref="AuthToken"/> is a
/// secret and is never logged, returned by an endpoint, or written to a source file.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string? AccountSid { get; set; }

    public string? AuthToken { get; set; }

    /// <summary>This application's own sending number. Immediate messages are sent from it, and reconciliation
    /// asks the provider only for this number's messages.</summary>
    public string? FromNumber { get; set; }

    /// <summary>Messaging Service used for scheduled (future-dated) messages, which the provider requires a
    /// Messaging Service for.</summary>
    public string? MessagingServiceSid { get; set; }

    /// <summary>Optional override for the messaging API base address. When set, it is used verbatim for every
    /// messaging-API call; when empty, the provider default is used.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>How far in the future the "how did delivery go?" follow-up is scheduled. A few days by default.</summary>
    public int FollowUpDelayHours { get; set; } = 72;
}
