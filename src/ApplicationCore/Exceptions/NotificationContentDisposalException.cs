using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a request to dispose of a notification's content could not be honoured at the
/// messaging provider. Because the guarantee is that the text is no longer retrievable from the
/// provider — not merely hidden locally — the local copy is left intact when this is raised so
/// the operator does not receive a false success.
/// </summary>
public class NotificationContentDisposalException : Exception
{
    public NotificationContentDisposalException(int notificationId, Exception inner)
        : base($"The content of notification {notificationId} could not be disposed of at the messaging provider.", inner)
    {
    }
}
