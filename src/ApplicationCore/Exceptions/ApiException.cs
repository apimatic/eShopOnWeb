using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base for exceptions that should surface to an API caller with a specific HTTP status code
/// and a safe, human-readable message (mapped by the PublicApi exception middleware).
/// </summary>
public abstract class ApiException : Exception
{
    protected ApiException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
