using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, int statusCode, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public bool IsClientError => StatusCode is >= 400 and < 500;

    public HttpStatusCode HttpStatusCode =>
        Enum.IsDefined(typeof(HttpStatusCode), StatusCode)
            ? (HttpStatusCode)StatusCode
            : HttpStatusCode.BadGateway;
}
