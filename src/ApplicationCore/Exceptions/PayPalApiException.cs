using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the PayPal API failed. Carries PayPal's error name and debug id so
/// operators can act on it; never carries card data.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? errorName, string message, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }
}

/// <summary>
/// PayPal requires an interactive (browser) step to complete the payment,
/// which this integration does not support.
/// </summary>
public class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException(string message) : base(message) { }
}
