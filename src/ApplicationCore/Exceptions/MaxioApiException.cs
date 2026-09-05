using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when Maxio Advanced Billing returns a non-success response. Carries the original
/// HTTP status code so callers can map it consistently (e.g. Maxio's 422 -> our 422).
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public MaxioApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
