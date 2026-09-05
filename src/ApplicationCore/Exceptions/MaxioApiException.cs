using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns an error response.
/// </summary>
public class MaxioApiException : Exception
{
    public int? StatusCode { get; }

    public MaxioApiException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}
