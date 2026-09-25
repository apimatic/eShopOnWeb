using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An application-level payment error carrying the HTTP status the caller should see (404 not found /
/// owned, 409 illegal state transition, 400 bad request, 402 payment required). Distinct from
/// <see cref="PayPalGatewayException"/>, which represents a PayPal-side failure.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }

    public static PaymentException NotFound(string message) => new(404, message);
    public static PaymentException Conflict(string message) => new(409, message);
    public static PaymentException BadRequest(string message) => new(400, message);
}
