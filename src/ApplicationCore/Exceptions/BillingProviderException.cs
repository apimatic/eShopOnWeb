using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(
        string message,
        bool isTransient,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        IsTransient = isTransient;
        StatusCode = statusCode;
    }

    public bool IsTransient { get; }
    public HttpStatusCode? StatusCode { get; }
}
