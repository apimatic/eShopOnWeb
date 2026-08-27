using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested notification operation conflicts with the current state of the
/// notification (for example resending to a contact number that has been removed).
/// </summary>
public class NotificationConflictException : Exception
{
    public NotificationConflictException(string message) : base(message)
    {
    }
}
