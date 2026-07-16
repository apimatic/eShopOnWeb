using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested action is illegal from a subscription's current lifecycle state (e.g. resuming
/// a subscription that is not paused), when a plan-change target is a no-op or unreachable, or when a plan
/// change is committed against a stale preview.
/// </summary>
public class InvalidSubscriptionStateException : Exception
{
    public InvalidSubscriptionStateException(string message) : base(message)
    {
    }
}
