using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class TwilioSettings
{
    public const string SectionName = "Twilio";

    public bool Enabled { get; set; } = true;

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    [Required]
    public string FromNumber { get; set; } = string.Empty;

    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }
}
