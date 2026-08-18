using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Non-secret messaging knobs the notification orchestration needs. Bound from the <c>Twilio:</c>
/// configuration section in the host. Holds no credentials.
/// </summary>
public class MessagingSettings
{
    /// <summary>The application's own configured sending number (Twilio:FromNumber), used to label the reconciliation report.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>How far ahead the "how did delivery go?" follow-up is queued with the provider. Defaults to 3 days.</summary>
    public TimeSpan FollowUpDelay { get; set; } = TimeSpan.FromDays(3);
}
