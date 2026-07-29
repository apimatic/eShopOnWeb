using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Raised when a Maxio billing operation cannot be completed. <see cref="StatusCode"/> is the HTTP
/// status the API layer should surface to the caller (e.g. 400 for a bad plan, 502 for an upstream failure).
/// </summary>
public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, int statusCode = 502, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>Suggested HTTP status code for the API response.</summary>
    public int StatusCode { get; }

    /// <summary>Any granular error messages returned by Maxio.</summary>
    public IReadOnlyList<string> Errors { get; }
}
