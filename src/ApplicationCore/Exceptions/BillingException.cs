using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for every failure raised by the subscription-billing capability. Callers can catch this
/// to distinguish an expected billing outcome from an unexpected application fault.
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
