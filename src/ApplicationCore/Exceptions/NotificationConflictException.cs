using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A notification operation that conflicts with current state (e.g. re-sending a message
/// whose destination is no longer registered, or whose content was disposed of).
/// </summary>
public class NotificationConflictException : Exception
{
    public NotificationConflictException(string message) : base(message)
    {
    }
}
