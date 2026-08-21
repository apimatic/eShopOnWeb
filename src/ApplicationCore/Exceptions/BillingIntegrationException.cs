using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingIntegrationException : Exception
{
    public BillingIntegrationException(string message, HttpStatusCode statusCode, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
