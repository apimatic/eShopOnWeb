using System;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

/// <summary>
/// Error raised when a call to the Maxio Advanced Billing API fails. Carries
/// a caller-safe message and the HTTP status to surface to the API consumer;
/// provider internals never leak onto the wire.
/// </summary>
public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// The HTTP status code the API should return for this failure.
    /// </summary>
    public int StatusCode { get; }
}
