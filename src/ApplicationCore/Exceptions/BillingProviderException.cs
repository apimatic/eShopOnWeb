using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Wraps a failure returned by, or while talking to, the external billing provider.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(
        string message,
        int? upstreamStatusCode = null,
        IEnumerable<string>? providerErrors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
        ProviderErrors = providerErrors?.ToArray() ?? Array.Empty<string>();
    }

    /// <summary>HTTP status the provider responded with, when the call reached it.</summary>
    public int? UpstreamStatusCode { get; }

    /// <summary>Validation or business messages returned by the provider, verbatim.</summary>
    public IReadOnlyCollection<string> ProviderErrors { get; }

    /// <summary>
    /// True when the provider rejected the request on its own rules (a 4xx other than throttling)
    /// rather than failing to serve it.
    /// </summary>
    public bool IsProviderRejection =>
        UpstreamStatusCode is >= 400 and < 500 && UpstreamStatusCode != 429;

    /// <summary>True when the provider throttled the request.</summary>
    public bool IsThrottled => UpstreamStatusCode == 429;
}
