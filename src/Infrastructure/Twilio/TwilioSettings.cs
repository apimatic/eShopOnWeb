using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; init; } = null!;

    [Required]
    public string AuthToken { get; init; } = null!;

    [Required]
    public string FromNumber { get; init; } = null!;

    [Required]
    public string MessagingServiceSid { get; init; } = null!;

    public string? BaseUrl { get; init; }
}
