using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when a notification operation is not valid for the notification's state
/// (e.g. resending a message whose content has already been disposed). Maps to HTTP 409.</summary>
public class NotificationConflictException : Exception
{
    public NotificationConflictException(string message) : base(message)
    {
    }
}
