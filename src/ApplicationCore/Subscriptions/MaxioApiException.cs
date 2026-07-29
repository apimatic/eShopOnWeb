using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Raised when a Maxio API call fails or when a subscribe request is invalid. Carries the
/// upstream HTTP status code (when applicable) and any error messages returned by Maxio.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(string message, int? statusCode = null, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>The upstream HTTP status code, when the failure originated from a Maxio response.</summary>
    public int? StatusCode { get; }

    /// <summary>Error messages extracted from the Maxio response body, if any.</summary>
    public IReadOnlyList<string> Errors { get; }
}
