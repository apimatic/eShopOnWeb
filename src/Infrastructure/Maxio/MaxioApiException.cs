using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio integration is misconfigured (missing API key, subdomain, etc.).
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}