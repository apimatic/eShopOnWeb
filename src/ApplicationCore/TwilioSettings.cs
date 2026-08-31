namespace Microsoft.eShopWeb;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Secrets (AccountSid, AuthToken)
/// arrive via user-secrets or environment variables — never from appsettings files.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }
    public string? FromNumber { get; set; }
    public string? MessagingServiceSid { get; set; }

    /// <summary>
    /// Optional override for the messaging API base address only. When set, it is used
    /// verbatim as the base address for every messaging-API call.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How many days after dispatch the delivery follow-up message is scheduled for.
    /// </summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
