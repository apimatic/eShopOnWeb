using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an interaction with the Maxio Advanced Billing API fails, either because
/// the upstream returned a non-success status or because the request could not be completed.
/// Carries the upstream status code (when available) and any validation messages Maxio returned
/// so callers can surface a meaningful response.
/// </summary>
public class MaxioBillingException : Exception
{
    /// <summary>The HTTP status code returned by Maxio, or null when the call never completed.</summary>
    public int? UpstreamStatusCode { get; }

    /// <summary>Validation / error messages extracted from the Maxio error payload, if any.</summary>
    public IReadOnlyList<string> Errors { get; }

    public MaxioBillingException(string message, int? upstreamStatusCode = null,
        IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(BuildMessage(message, errors), innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    private static string BuildMessage(string message, IReadOnlyList<string>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return message;
        }

        return $"{message} ({string.Join("; ", errors.Where(e => !string.IsNullOrWhiteSpace(e)))})";
    }
}
