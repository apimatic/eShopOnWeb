using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A concurrent subscription request for the same plan is already in progress. The caller may retry;
/// the in-flight request will have settled by then.
/// </summary>
public class SubscriptionConflictException : Exception
{
    public SubscriptionConflictException(string message) : base(message)
    {
    }
}
