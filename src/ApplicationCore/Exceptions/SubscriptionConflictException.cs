using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

// The request is well-formed but conflicts with the subscription's current state: a no-op plan
// change, an illegal lifecycle transition, or a plan-change commit whose preview went stale.
public class SubscriptionConflictException : Exception
{
    public SubscriptionConflictException(string message) : base(message)
    {
    }
}
