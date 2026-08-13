using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operation requires message content that has already been disposed at the
/// shopper's request (maps to HTTP 409).
/// </summary>
public class NotificationContentDisposedException : Exception
{
    public NotificationContentDisposedException(string message) : base(message)
    {
    }
}
