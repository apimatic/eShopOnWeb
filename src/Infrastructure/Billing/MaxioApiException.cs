using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message, bool isTransient = false,
        Exception? innerException = null) : base(message, innerException)
    {
        StatusCode = statusCode;
        IsTransient = isTransient;
    }

    public HttpStatusCode StatusCode { get; }
    public bool IsTransient { get; }
}
