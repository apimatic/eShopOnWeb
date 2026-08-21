using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }

    public bool IsUnprocessableEntity => (int)StatusCode == 422;
}
