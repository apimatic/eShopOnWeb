using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal rejected or failed an API call. Carries PayPal's error name, the
/// fine-grained issue codes and the debug id for correlation with PayPal.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string? errorName, string message, string? debugId, string? issues = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
        Issues = issues;
    }

    public int StatusCode { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }
    public string? Issues { get; }
}
