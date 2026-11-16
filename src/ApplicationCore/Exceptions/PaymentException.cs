using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }

    public static PaymentException BadRequest(string message) => new(400, message);
    public static PaymentException Forbidden(string message) => new(403, message);
    public static PaymentException NotFound(string message) => new(404, message);
    public static PaymentException Conflict(string message) => new(409, message);
    public static PaymentException Unprocessable(string message) => new(422, message);
    public static PaymentException Upstream(string message) => new(502, message);
}
