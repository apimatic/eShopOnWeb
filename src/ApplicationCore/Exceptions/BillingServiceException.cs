using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the external billing system of record could not fulfil a request (network
/// failure, unexpected status, or a rejected operation). Surfaced to the API as a 502.
/// </summary>
public class BillingServiceException : Exception
{
    public BillingServiceException(string message) : base(message)
    {
    }

    public BillingServiceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
