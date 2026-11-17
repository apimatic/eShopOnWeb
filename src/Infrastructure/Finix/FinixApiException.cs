using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public class FinixApiException : Exception
{
    public FinixApiException(HttpStatusCode statusCode, string? code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }
    public string? Code { get; }
}
