using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a caller's notification/order request is invalid (an empty order, a non-positive
/// quantity, an unknown catalog item, or a resend of a message whose content has been disposed).
/// The API boundary maps it to 400 Bad Request.
/// </summary>
public class NotificationValidationException : Exception
{
    public NotificationValidationException(string message) : base(message) { }
}
