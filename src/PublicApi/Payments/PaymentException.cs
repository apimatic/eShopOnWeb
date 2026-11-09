using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Raised when the app cannot carry out a request; surfaces as a 4xx/5xx status with the reason as the message.
/// </summary>
public class PaymentException : Exception
{
    public int StatusCode { get; }

    public PaymentException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static PaymentException BadRequest(string message) => new(400, message);
    public static PaymentException NotFound(string message) => new(404, message);
    public static PaymentException Conflict(string message) => new(409, message);
    public static PaymentException Unprocessable(string message) => new(422, message);
}
