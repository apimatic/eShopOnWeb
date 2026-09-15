using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Strongly-typed Twilio messaging configuration, bound from the <c>Twilio:</c> section. Values are
/// supplied by configuration (user-secrets / environment) and never hard-coded — the same build must run
/// against a different Twilio account.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number; reconciliation counts only messages from this number.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service used for scheduled sends (scheduling requires a Messaging Service).</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set, it is used verbatim for every
    /// messaging-API call; it does not govern the Lookup host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How many days after dispatch the delivery follow-up is queued with the provider.</summary>
    public double FollowUpDelayDays { get; set; } = 3;

    /// <summary>Whole-call budget for a single provider request.</summary>
    public int RequestTimeoutSeconds { get; set; } = 20;

    /// <summary>Returns the names of any required settings that are missing (for a start-up guard).</summary>
    public IReadOnlyList<string> MissingRequiredKeys()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(AccountSid)) missing.Add($"{SectionName}:{nameof(AccountSid)}");
        if (string.IsNullOrWhiteSpace(AuthToken)) missing.Add($"{SectionName}:{nameof(AuthToken)}");
        if (string.IsNullOrWhiteSpace(FromNumber)) missing.Add($"{SectionName}:{nameof(FromNumber)}");
        if (string.IsNullOrWhiteSpace(MessagingServiceSid)) missing.Add($"{SectionName}:{nameof(MessagingServiceSid)}");
        return missing;
    }
}
