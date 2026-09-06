using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing.Exceptions;

/// <summary>
/// Base type for every failure surfaced by the subscription billing capability. Callers can
/// catch this to handle "billing went wrong" without depending on a particular provider.
/// </summary>
public abstract class BillingException : Exception
{
    protected BillingException(string message) : base(message)
    {
    }

    protected BillingException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
