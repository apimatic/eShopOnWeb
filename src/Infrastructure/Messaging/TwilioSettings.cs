using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSettings : IOrderNotificationSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    [Required]
    public string FromNumber { get; set; } = string.Empty;

    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    public int FollowUpDelayDays { get; set; } = 3;
}
