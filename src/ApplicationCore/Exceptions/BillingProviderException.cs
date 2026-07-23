using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider rejects a call or cannot be reached. This is the single typed
/// error the provider seam surfaces, so callers never have to know that the provider speaks HTTP.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message)
        : this(message, statusCode: null, providerErrors: null, innerException: null)
    {
    }

    public BillingProviderException(string message, Exception? innerException)
        : this(message, statusCode: null, providerErrors: null, innerException: innerException)
    {
    }

    public BillingProviderException(string message,
        int? statusCode,
        IEnumerable<string>? providerErrors,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors?.ToList() ?? new List<string>();
    }

    /// <summary>The provider's HTTP status code, when the failure reached the provider at all.</summary>
    public int? StatusCode { get; }

    /// <summary>Messages the provider returned, suitable for surfacing to the actor.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }
}
