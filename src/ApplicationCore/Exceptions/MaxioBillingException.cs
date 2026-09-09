using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio Advanced Billing integration fails. Carries the provider's HTTP
/// status when the failure is a client error the caller can act on; a null status means the
/// outcome is unknown (transport failure or unreadable provider response) and callers should
/// surface a 5xx rather than retry-as-client-error.
/// </summary>
public class MaxioBillingException : Exception
{
    public int? StatusCode { get; }

    public MaxioBillingException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
