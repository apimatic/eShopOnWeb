using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing system of record rejects a request or cannot be reached.
/// <see cref="StatusCode"/> carries the HTTP status the API should surface to its own callers.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int statusCode = 502, IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors?.ToList() ?? new List<string>();
    }

    /// <summary>HTTP status code to return to the eShopOnWeb caller.</summary>
    public int StatusCode { get; }

    /// <summary>Provider-supplied validation messages, when any were returned.</summary>
    public IReadOnlyList<string> Errors { get; }
}
