using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An expected, caller-actionable payment failure. The API middleware maps this to an HTTP response.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode = 400, string code = "PAYMENT_ERROR",
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }

    public static PaymentException BadRequest(string message, string code = "PAYMENT_BAD_REQUEST") =>
        new(message, 400, code);

    public static PaymentException NotFound(string message, string code = "PAYMENT_NOT_FOUND") =>
        new(message, 404, code);

    public static PaymentException Conflict(string message, string code = "PAYMENT_CONFLICT") =>
        new(message, 409, code);

    public static PaymentException Upstream(string message, Exception? inner = null) =>
        new(message, 502, "FINIX_UPSTREAM_ERROR", inner);
}
