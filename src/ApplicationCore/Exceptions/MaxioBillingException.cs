using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a Maxio billing operation fails. Carries the HTTP status code and
/// any error messages returned by Maxio so the API layer can surface a clean,
/// meaningful response to the caller.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, int? statusCode = null, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(BuildMessage(message, errors), innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>The upstream HTTP status code, when the failure came from Maxio.</summary>
    public int? StatusCode { get; }

    /// <summary>Any error messages returned by Maxio.</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(string message, IReadOnlyList<string>? errors)
    {
        if (errors is { Count: > 0 })
        {
            return $"{message}: {string.Join("; ", errors.Where(e => !string.IsNullOrWhiteSpace(e)))}";
        }

        return message;
    }
}
