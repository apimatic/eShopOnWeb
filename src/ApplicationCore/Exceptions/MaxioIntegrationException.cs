using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a call to the Maxio billing API fails or returns an unexpected
/// response. Carries the upstream HTTP status code (when available) and any
/// error messages Maxio returned, so callers can surface an accurate result.
/// </summary>
public class MaxioIntegrationException : Exception
{
    public MaxioIntegrationException(string message, int? statusCode = null, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(BuildMessage(message, errors), innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>The HTTP status code returned by Maxio, if the failure came from a response.</summary>
    public int? StatusCode { get; }

    /// <summary>Error messages extracted from the Maxio response body.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True when the failure is attributable to the caller's input (e.g. 4xx) rather than an upstream fault.</summary>
    public bool IsClientError => StatusCode is >= 400 and < 500;

    private static string BuildMessage(string message, IReadOnlyList<string>? errors)
        => errors is { Count: > 0 } ? $"{message}: {string.Join("; ", errors)}" : message;
}
