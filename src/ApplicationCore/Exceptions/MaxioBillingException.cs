using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the subscription-billing provider rejects a request or its outcome
/// cannot be confirmed. <see cref="StatusCode"/> carries the HTTP status the caller
/// should see: provider 4xx statuses pass through unchanged; transport failures,
/// unreadable responses and provider-side outages surface as 5xx.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(int statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>The HTTP status code to surface to the API caller.</summary>
    public int StatusCode { get; }

    /// <summary>Provider validation messages, when the rejection carried any.</summary>
    public IReadOnlyList<string> Errors { get; }
}