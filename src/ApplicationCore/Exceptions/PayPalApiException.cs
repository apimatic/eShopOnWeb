using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to PayPal failed. Carries PayPal's error name/issue and debug id so
/// operators can act on it. Never contains card details or credentials.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string? errorName, string? issue, string? debugId, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issue = issue;
        DebugId = debugId;
    }

    public int StatusCode { get; }
    public string? ErrorName { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}
