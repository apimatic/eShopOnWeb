using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Caller-safe billing failure. <see cref="StatusCode"/> is the HTTP status to return
/// at the PublicApi boundary (provider 4xx kept distinct from transport/unknown 5xx).
/// </summary>
public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
