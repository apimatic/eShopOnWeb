using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal rejected or failed an API call. Carries PayPal's error name/issue/debug id
/// for correlation. Never contains card data.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? errorName, string? issue,
        string message, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issue = issue;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ErrorName { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}
