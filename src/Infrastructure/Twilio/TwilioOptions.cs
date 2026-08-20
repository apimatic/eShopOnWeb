using System;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Strongly-typed Twilio settings, bound from the <c>Twilio:</c> configuration section.
/// None of these values are ever hard-coded; the same build runs against any account.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    /// <summary>Twilio default host for the messaging (v2010) API.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>Twilio host for the Lookups v2 API (not governed by <see cref="BaseUrl"/>).</summary>
    public static readonly string LookupsBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } o
            ? o
            : "https://lookups.twilio.com";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned, never written to a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    public string FromNumber { get; set; } = string.Empty;

    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the messaging API base URL. When empty, the default host is used.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The base URL every messaging-API call should use.</summary>
    public string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl.TrimEnd('/');

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken);
}
