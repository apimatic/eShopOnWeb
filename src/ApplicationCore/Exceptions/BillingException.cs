using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an interaction with the billing system of record (Maxio) fails in a way the caller
/// should be told about (e.g. an unknown plan, or an upstream error). Carries an HTTP-ish status
/// code so the API layer can translate it into an appropriate response.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>Suggested HTTP status code for surfacing this failure to an API caller.</summary>
    public int StatusCode { get; }
}
