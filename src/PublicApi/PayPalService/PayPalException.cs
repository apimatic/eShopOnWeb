using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.PayPalService;

public class PayPalException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public PayPalException(string message, HttpStatusCode statusCode = HttpStatusCode.BadGateway)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public PayPalException(string message, Exception inner, HttpStatusCode statusCode = HttpStatusCode.BadGateway)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
