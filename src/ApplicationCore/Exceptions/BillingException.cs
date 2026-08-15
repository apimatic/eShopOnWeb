using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the external billing system (Maxio) rejects a request or is unavailable.
/// Carries an optional HTTP status code so the API layer can translate it into a response.
/// </summary>
public class BillingException : Exception
{
    public int? StatusCode { get; }

    public BillingException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
