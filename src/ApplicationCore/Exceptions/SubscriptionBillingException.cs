using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system of record rejected a request or could not be reached. Callers should treat
/// this as an upstream failure rather than as bad input.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int? upstreamStatusCode = null,
        IReadOnlyList<string>? upstreamErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
        UpstreamErrors = upstreamErrors ?? Array.Empty<string>();
    }

    /// <summary>HTTP status the billing system returned, when the call got that far.</summary>
    public int? UpstreamStatusCode { get; }

    /// <summary>Error messages the billing system returned, if any.</summary>
    public IReadOnlyList<string> UpstreamErrors { get; }
}
