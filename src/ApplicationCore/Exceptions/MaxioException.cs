using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio Advanced Billing provider rejects a request or cannot be reached.
/// </summary>
public class MaxioException : Exception
{
    /// <summary>
    /// The HTTP status code that should be surfaced to callers of eShopOnWeb.
    /// </summary>
    public int StatusCode { get; }

    public MaxioException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public MaxioException(string message, int statusCode, Exception innerException) : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
