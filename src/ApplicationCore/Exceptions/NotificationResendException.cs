using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an operator re-send cannot proceed — for example the message was already delivered, or its
/// content has been disposed of and there is nothing to re-send. Surfaced to the API as a 409 Conflict.
/// </summary>
public class NotificationResendException : Exception
{
    public NotificationResendException(string message) : base(message) { }
}
