using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment-flow failure the caller can act on. StatusCode is the HTTP status the API
/// should return (e.g. 404 for someone else's order, 409 for an invalid state transition,
/// 422 when PayPal declines or can no longer renew an authorization).
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
