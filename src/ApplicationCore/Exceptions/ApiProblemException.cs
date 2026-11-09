using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a request cannot be carried out; the API answers with <see cref="StatusCode"/> and the message as the reason.
/// </summary>
public class ApiProblemException : Exception
{
    public int StatusCode { get; }

    public ApiProblemException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
