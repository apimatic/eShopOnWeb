using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operation needs the text of a message whose content has been disposed of.
/// </summary>
public class NotificationContentRedactedException : Exception
{
    public NotificationContentRedactedException(string message) : base(message)
    {
    }
}
