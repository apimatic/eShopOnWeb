using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a notification operation is not permissible in the notification's current state
/// (for example, re-sending a message whose content has already been disposed of).
/// </summary>
public class NotificationOperationException : Exception
{
    public NotificationOperationException(string message) : base(message) { }
}
