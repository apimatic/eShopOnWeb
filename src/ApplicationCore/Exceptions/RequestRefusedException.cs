using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The app cannot carry out a request. The API answers with <see cref="StatusCode"/> and the message as the reason.
/// </summary>
public class RequestRefusedException : Exception
{
    public int StatusCode { get; }

    public RequestRefusedException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
