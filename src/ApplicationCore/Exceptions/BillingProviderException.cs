using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider rejects an operation or cannot be reached. This is the single
/// typed error the provider seam surfaces, so callers never see transport-level exception types.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
        ProviderErrors = Array.Empty<string>();
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
        ProviderErrors = Array.Empty<string>();
    }

    public BillingProviderException(string message, int? statusCode, IEnumerable<string>? providerErrors) : base(message)
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors?.ToArray() ?? Array.Empty<string>();
    }

    /// <summary>The HTTP status code the provider returned, or null if the call never completed.</summary>
    public int? StatusCode { get; }

    /// <summary>The messages the provider reported, verbatim, so they can be surfaced to the actor.</summary>
    public IReadOnlyCollection<string> ProviderErrors { get; }
}
