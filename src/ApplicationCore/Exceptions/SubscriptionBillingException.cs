using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised for client-correctable billing problems (unknown plan, missing identity, etc.).
/// </summary>
public class SubscriptionBillingException : Exception
{
    public int StatusCode { get; }

    public SubscriptionBillingException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}
