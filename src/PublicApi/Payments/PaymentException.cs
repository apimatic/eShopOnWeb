using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The app cannot carry out a request: answered with the status code and the reason as message.
/// </summary>
public class PaymentException : Exception
{
    public int StatusCode { get; }

    public PaymentException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static PaymentException BadRequest(string message) => new((int)HttpStatusCode.BadRequest, message);
    public static PaymentException NotFound(string message) => new((int)HttpStatusCode.NotFound, message);
    public static PaymentException Conflict(string message) => new((int)HttpStatusCode.Conflict, message);
    public static PaymentException Unprocessable(string message) => new(422, message);
}
