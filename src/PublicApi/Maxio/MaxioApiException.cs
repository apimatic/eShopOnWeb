using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when Maxio responds with an unexpected or unsuccessful status code.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>The HTTP status code returned by the Maxio API (0 when unknown).</summary>
    public int StatusCode { get; }

    /// <summary>Human readable errors reported by the Maxio API, if any.</summary>
    public IReadOnlyList<string> Errors { get; }
}
