using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system of record could not complete the request. Callers should treat this as an
/// upstream dependency failure rather than a client mistake.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message) : base(message)
    {
    }

    public SubscriptionBillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
