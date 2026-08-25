using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when Maxio Advanced Billing rejects a request or returns an unexpected response.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, IReadOnlyList<string> errors)
        : base($"Maxio API request failed with status {statusCode}: {string.Join("; ", errors)}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public MaxioApiException(int statusCode, string message)
        : this(statusCode, new List<string> { message })
    {
    }

    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }
}
