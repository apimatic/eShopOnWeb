using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects or fails a call. Carries only the provider's own
/// messages - never the request that produced them, so credentials can never travel with it.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : this(message, null, null)
    {
    }

    public BillingProviderException(string message, int? statusCode, IEnumerable<string>? providerErrors, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors?.ToList() ?? new List<string>();
    }

    /// <summary>
    /// The HTTP status the provider responded with, when the call reached it at all.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// The provider's own error messages, safe to surface to the caller.
    /// </summary>
    public IReadOnlyCollection<string> ProviderErrors { get; }
}
