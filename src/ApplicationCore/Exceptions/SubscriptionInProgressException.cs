using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Another request for the same user is already creating the same billing record.</summary>
public class SubscriptionInProgressException : Exception
{
    public SubscriptionInProgressException(string message) : base(message)
    {
    }
}
