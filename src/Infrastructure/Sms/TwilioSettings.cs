using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio configuration, bound from the <c>Twilio:</c> section. Values are supplied at deployment
/// time (env vars → user-secrets); none are hard-coded, so the same build runs against a different
/// Twilio account. The auth token is a secret and is never logged or returned by any endpoint.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL (the API messages are sent, read and
    /// reconciled through). When set, it is used verbatim as the base address for every
    /// messaging-API call. It does not govern other Twilio hosts (e.g. Lookups).
    /// </summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Refuses to start the host when any required Twilio credential is missing or blank — a blank
/// part is not the same as a missing one, so each is checked individually. The failure names the
/// config key and never echoes a value.
/// </summary>
public sealed class TwilioSettingsValidator : IValidateOptions<TwilioSettings>
{
    public ValidateOptionsResult Validate(string? name, TwilioSettings options)
    {
        var failures = new System.Collections.Generic.List<string>();

        void Require(string value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                failures.Add($"{TwilioSettings.SectionName}:{key} is not configured. Set it via " +
                    "environment variable or .NET user-secrets before starting the app.");
            }
        }

        Require(options.AccountSid, nameof(TwilioSettings.AccountSid));
        Require(options.AuthToken, nameof(TwilioSettings.AuthToken));
        Require(options.FromNumber, nameof(TwilioSettings.FromNumber));
        Require(options.MessagingServiceSid, nameof(TwilioSettings.MessagingServiceSid));

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
