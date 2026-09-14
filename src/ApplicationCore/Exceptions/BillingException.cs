using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for every failure raised by the subscription-billing capability.
/// </summary>
public abstract class BillingException : Exception
{
    protected BillingException(string message) : base(message)
    {
    }

    protected BillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
