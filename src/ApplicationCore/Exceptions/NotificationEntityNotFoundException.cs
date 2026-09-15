using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a notification-related entity (order, notification, contact number) cannot be
/// found, or is not visible to the caller. Surfaced as HTTP 404.
/// </summary>
public class NotificationEntityNotFoundException : Exception
{
    public NotificationEntityNotFoundException(string message) : base(message)
    {
    }
}
