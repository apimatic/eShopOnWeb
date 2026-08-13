using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// The shape of a notification as returned by the read endpoints. Carries the identifier the operator
/// endpoints act on (<see cref="NotificationId"/>) and where the message got to (<see cref="Status"/>,
/// <see cref="ErrorCode"/>). The shopper's number is deliberately never included.
/// </summary>
public record NotificationView(
    int NotificationId,
    int OrderId,
    string Type,
    string Status,
    string? MessageSid,
    bool IsScheduled,
    DateTimeOffset? ScheduledFor,
    int? ErrorCode,
    string? ErrorMessage,
    bool ContentDisposed,
    DateTimeOffset CreatedAt)
{
    public static NotificationView From(SmsNotification n) => new(
        n.Id, n.OrderId, n.Type.ToString(), n.Status, n.MessageSid, n.IsScheduled,
        n.ScheduledFor, n.ErrorCode, n.ErrorMessage, n.ContentDisposed, n.CreatedAt);
}

/// <summary>Helpers for reading the caller's identity out of the validated JWT.</summary>
public static class CallerIdentity
{
    /// <summary>The signed-in shopper's identity (their user name), or null when unauthenticated.</summary>
    public static string? GetOwnerId(ClaimsPrincipal user)
        => user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
}
