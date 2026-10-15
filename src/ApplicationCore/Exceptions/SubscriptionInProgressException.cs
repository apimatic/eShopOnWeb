using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Another request for the same user (and plan) is already talking to the billing provider.</summary>
public class SubscriptionInProgressException : Exception
{
    public SubscriptionInProgressException(string message) : base(message)
    {
    }
}
