using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a resend is requested for a message whose content has already been disposed of, so there
/// is no longer any text to send.
/// </summary>
public class ContentAlreadyDisposedException : Exception
{
    public ContentAlreadyDisposedException()
        : base("The message content has been disposed of and can no longer be resent.") { }
}
