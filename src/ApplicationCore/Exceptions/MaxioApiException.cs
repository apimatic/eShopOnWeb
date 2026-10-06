using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio Advanced Billing API answers a non-success status.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>HTTP status returned by the billing system; 0 for transport failures.</summary>
    public int StatusCode { get; }

    /// <summary>Parsed validation messages from the billing system's error payload.</summary>
    public IReadOnlyList<string> Errors { get; }
}
