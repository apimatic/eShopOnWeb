using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal's API returned an error we could not turn into a domain outcome. Carries the upstream
/// status code and PayPal's own error payload so operators can act on it.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(string message, HttpStatusCode statusCode, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }
}
