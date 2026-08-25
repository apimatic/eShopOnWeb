using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public class PayPalException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public PayPalException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
