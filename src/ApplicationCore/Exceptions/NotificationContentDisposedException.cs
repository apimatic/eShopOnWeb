using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an operation needs a message's content but it has already been disposed of.</summary>
public class NotificationContentDisposedException : Exception
{
    public NotificationContentDisposedException(string message) : base(message)
    {
    }
}
