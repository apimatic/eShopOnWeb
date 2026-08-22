using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderPaymentException : Exception
{
    public OrderPaymentException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }

    public static OrderPaymentException BadRequest(string message) => new(400, message);
    public static OrderPaymentException Forbidden(string message) => new(403, message);
    public static OrderPaymentException NotFound(string message) => new(404, message);
    public static OrderPaymentException Conflict(string message) => new(409, message);
    public static OrderPaymentException Unprocessable(string message) => new(422, message);
}
