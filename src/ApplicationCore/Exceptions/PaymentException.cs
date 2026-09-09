using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation that failed for a reason the caller/operator can act on (e.g. the order is in
/// the wrong state, a refund would exceed the captured amount, or an authorization can no longer be
/// renewed). Carries the HTTP status the API should surface.
/// </summary>
public class PaymentException : Exception
{
    public int StatusCode { get; }

    public PaymentException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public PaymentException(string message, Exception innerException, int statusCode = 400)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
