using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio billing provider rejects a request or cannot be reached.
/// </summary>
public class MaxioBillingException : Exception
{
    /// <summary>
    /// The HTTP status this failure should surface as at the API boundary:
    /// 400 for a validation rejection the caller can act on, 502 for anything
    /// upstream (unreachable provider, unparseable response, unexpected error).
    /// </summary>
    public int StatusCode { get; }

    public MaxioBillingException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
