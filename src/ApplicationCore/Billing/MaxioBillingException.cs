using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Raised when a call to the Maxio Advanced Billing API fails. Carries the HTTP
/// status code and any error messages returned by Maxio so callers can translate
/// them into an appropriate API response.
/// </summary>
public class MaxioBillingException : Exception
{
    public int? StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public MaxioBillingException(string message, int? statusCode = null, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(BuildMessage(message, errors), innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    private static string BuildMessage(string message, IReadOnlyList<string>? errors)
    {
        if (errors is { Count: > 0 })
        {
            return $"{message} ({string.Join("; ", errors)})";
        }

        return message;
    }
}

/// <summary>
/// Raised when the Maxio integration is invoked but its required configuration
/// (API key, subdomain/base URL, product family handle) is missing.
/// </summary>
public class MaxioConfigurationException : MaxioBillingException
{
    public MaxioConfigurationException(string message)
        : base(message)
    {
    }
}
