using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a notification operation is not valid for the notification's current state
/// (e.g. resending a message whose content was disposed of, or whose recipient number was removed).
/// </summary>
public class InvalidNotificationOperationException : Exception
{
    public InvalidNotificationOperationException(string message) : base(message)
    {
    }
}
