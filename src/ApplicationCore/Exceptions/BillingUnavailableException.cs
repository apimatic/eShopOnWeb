using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system could not be reached, timed out, or kept throttling us after every retry.
/// The request may be safely retried later.
/// </summary>
public class BillingUnavailableException : BillingException
{
    public BillingUnavailableException(string message) : base(message)
    {
    }

    public BillingUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
