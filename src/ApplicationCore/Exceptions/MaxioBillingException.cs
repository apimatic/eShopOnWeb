using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a Maxio billing operation fails. Carries a best-effort HTTP status to return
/// to the caller: a 4xx surfaces a caller-correctable problem (e.g. an unknown plan handle or
/// a validation error from Maxio), while 502 signals an upstream/billing-system failure.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status code to surface to the API caller.</summary>
    public int StatusCode { get; }
}
