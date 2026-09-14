using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider rejected a request, or could not be reached.
/// </summary>
/// <remarks>
/// Carries the provider status code and the provider-supplied validation messages so that the API
/// layer can decide between "the caller sent something invalid" (4xx) and "the downstream billing
/// system is unhealthy" (502/504) without re-parsing provider payloads.
/// </remarks>
public class BillingProviderException : Exception
{
    public BillingProviderException(
        string message,
        int? providerStatusCode = null,
        IReadOnlyList<string>? providerErrors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
    }

    /// <summary>HTTP status the provider responded with, when a response was received at all.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>Validation messages reported by the provider, if any.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }

    /// <summary>
    /// <c>true</c> when the provider rejected the request because of its content, meaning the
    /// caller can act on the failure. <c>false</c> for authentication, rate limit and server side
    /// failures, which the caller cannot fix.
    /// </summary>
    public bool IsCallerFault =>
        ProviderStatusCode is 400 or 404 or 422;

    public override string ToString() => ProviderErrors.Any()
        ? $"{base.ToString()}{Environment.NewLine}Provider errors: {string.Join("; ", ProviderErrors)}"
        : base.ToString();
}
