using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>A failure already translated to the status the caller should see. Message is caller-safe.</summary>
public class SubscriptionException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> Details { get; }

    /// <summary>True when a write may have reached Maxio even though the call failed.</summary>
    public bool OutcomeUnknown { get; }

    public SubscriptionException(int statusCode, string message, Exception? inner = null,
        IReadOnlyList<string>? details = null, bool outcomeUnknown = false)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Details = details ?? Array.Empty<string>();
        OutcomeUnknown = outcomeUnknown;
    }
}
