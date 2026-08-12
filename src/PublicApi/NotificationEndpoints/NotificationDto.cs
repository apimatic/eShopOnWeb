using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The shape of a notification as returned by the API. Carries the provider's identifier and current
/// delivery outcome so a later request can act on and report on it. The destination is masked so a
/// full number is not spread through responses.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;

    /// <summary>This application's delivery outcome (see <see cref="NotificationStatus"/>).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The provider's own identifier for the message (message SID), if one was created.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>The provider's raw status string, kept verbatim.</summary>
    public string? ProviderStatus { get; set; }

    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }

    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }

    public bool ContentRedacted { get; set; }

    /// <summary>Destination in masked form (e.g. +1******4987); null when no number was on file.</summary>
    public string? ToMasked { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status.ToString(),
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderStatus = n.ProviderStatusRaw,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        IsScheduled = n.IsScheduled,
        ScheduledFor = n.ScheduledFor,
        ProviderDateSent = n.ProviderDateSent,
        ContentRedacted = n.ContentRedacted,
        ToMasked = MaskNumber(n.ToPhoneNumber),
        CreatedAt = n.CreatedAt
    };

    /// <summary>Keeps a leading '+' and the last two digits, masking the rest.</summary>
    public static string? MaskNumber(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return null;
        }

        var digitsShown = 2;
        if (number.Length <= digitsShown + 1)
        {
            return new string('*', number.Length);
        }

        var prefix = number[0] == '+' ? "+" : string.Empty;
        var suffix = number[^digitsShown..];
        var maskedCount = number.Length - prefix.Length - digitsShown;
        return prefix + new string('*', maskedCount) + suffix;
    }
}
