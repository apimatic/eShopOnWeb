using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required(AllowEmptyStrings = false)]
    public string AccountSid { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string AuthToken { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string FromNumber { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string MessagingServiceSid { get; init; } = string.Empty;

    public string? BaseUrl { get; init; }
}
