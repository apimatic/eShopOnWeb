namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values arrive via
/// user-secrets/environment variables and are never written to source control.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string? MessagingServiceSid { get; set; }

    /// <summary>
    /// Optional override for the messaging API host only (send/fetch/list/update messages).
    /// Other Twilio capabilities (e.g. Lookup) keep their own hosts.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string EffectiveBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? TwilioMessagingClient.DefaultBaseUrl : BaseUrl!.TrimEnd('/');

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccountSid) || string.IsNullOrWhiteSpace(AuthToken) || string.IsNullOrWhiteSpace(FromNumber))
        {
            throw new System.InvalidOperationException(
                "Twilio settings are not configured. Provide Twilio:AccountSid, Twilio:AuthToken and Twilio:FromNumber via user-secrets or environment variables.");
        }
    }
}
