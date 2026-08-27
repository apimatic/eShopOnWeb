using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested notification operation conflicts with the current state
/// (e.g. resending to a number no longer on file, or resending disposed content).
/// </summary>
public class NotificationConflictException : Exception
{
    public NotificationConflictException(string message) : base(message)
    {
    }
}
