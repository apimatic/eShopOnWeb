using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest, string? issue = null)
        : base(message)
    {
        StatusCode = statusCode;
        Issue = issue;
    }

    public HttpStatusCode StatusCode { get; }
    public string? Issue { get; }
}
