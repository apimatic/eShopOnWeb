using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a Maxio billing operation fails. Carries an HTTP-ish status hint so the
/// API layer can translate it into an appropriate response without knowing about the SDK.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    public BillingException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>Optional upstream status code (e.g. 404, 422) when known; null otherwise.</summary>
    public int? StatusCode { get; }
}
