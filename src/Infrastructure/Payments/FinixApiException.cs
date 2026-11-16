using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class FinixApiException : Exception
{
    public FinixApiException(HttpStatusCode statusCode, string message, string? code = null, string? logref = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Logref = logref;
    }

    public HttpStatusCode StatusCode { get; }
    public string? Code { get; }
    public string? Logref { get; }
}
