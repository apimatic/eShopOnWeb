using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the upstream billing provider (Maxio) returns an error or is unreachable.
/// Carries the upstream HTTP status (when available) and any provider-supplied error messages
/// so the API layer can surface a meaningful, non-leaky response.
/// </summary>
public class BillingProviderException : BillingException
{
    public int? UpstreamStatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public BillingProviderException(string message, int? upstreamStatusCode = null, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        UpstreamStatusCode = upstreamStatusCode;
        Errors = errors ?? new List<string>();
    }

    public BillingProviderException(string message, Exception innerException, int? upstreamStatusCode = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
        Errors = new List<string>();
    }
}
