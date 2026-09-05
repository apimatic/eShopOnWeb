using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a call to the Maxio Advanced Billing API fails or returns an unexpected shape.
/// </summary>
public class MaxioApiException : Exception
{
    public int? StatusCode { get; }

    public MaxioApiException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}
