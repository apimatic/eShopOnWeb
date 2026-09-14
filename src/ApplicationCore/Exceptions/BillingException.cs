using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for errors originating from the subscription-billing capability.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message) : base(message)
    {
    }

    public BillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
