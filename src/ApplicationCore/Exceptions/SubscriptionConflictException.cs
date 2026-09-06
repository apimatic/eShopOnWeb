using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A subscribe attempt collided with another in-flight attempt and we could not determine
/// whether it took effect. Retrying once the first attempt settles is safe.
/// </summary>
public class SubscriptionConflictException : Exception
{
    public SubscriptionConflictException(string message) : base(message)
    {
    }
}
