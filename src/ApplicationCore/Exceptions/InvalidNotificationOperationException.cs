using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operator action on a notification cannot be carried out for a reason the caller
/// can act on — for example re-sending a message whose content has been disposed of, or whose
/// destination number the shopper has since removed.
/// </summary>
public class InvalidNotificationOperationException : Exception
{
    public InvalidNotificationOperationException(string message) : base(message)
    {
    }
}
