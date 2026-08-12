using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// Presentation helpers shared by the notification, order and contact-number endpoints: resolving the
/// caller's identity from the token, masking numbers so full numbers never leave the system, and
/// normalizing a notification's outcome for display.
/// </summary>
public static class NotificationPresentation
{
    /// <summary>The signed-in caller's identity (user name), taken from the JWT. Present for any authenticated request.</summary>
    public static string CallerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(name))
        {
            throw new UnauthorizedAccessException("The caller's identity could not be determined from the token.");
        }
        return name;
    }

    /// <summary>Masks a phone number to its last four digits so the full number is never exposed or logged.</summary>
    public static string Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return string.Empty;
        }
        var lastFour = number.Length <= 4 ? number : number[^4..];
        return "••••" + lastFour;
    }

    /// <summary>A single normalized outcome word for a notification.</summary>
    public static string DeliveryStatus(OrderNotification n)
    {
        if (n.ScheduleCancelled)
        {
            return "cancelled";
        }
        if (n.SendFailureReason != null)
        {
            return "send_failed";
        }
        if (n.IsScheduled)
        {
            return "scheduled";
        }
        return n.ProviderStatus ?? "unknown";
    }
}
