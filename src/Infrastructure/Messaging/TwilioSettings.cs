using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; init; } = string.Empty;

    [Required]
    public string AuthToken { get; init; } = string.Empty;

    [Required]
    public string FromNumber { get; init; } = string.Empty;

    [Required]
    public string MessagingServiceSid { get; init; } = string.Empty;

    public string? BaseUrl { get; init; }
}
