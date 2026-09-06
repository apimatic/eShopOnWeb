using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system rejected a request or could not be reached.
/// </summary>
public class BillingProviderException : BillingException
{
    public BillingProviderException(string message, int? providerStatusCode = null, IEnumerable<string>? providerErrors = null)
        : base(message)
    {
        ProviderStatusCode = providerStatusCode;
        ProviderErrors = providerErrors?.ToArray() ?? Array.Empty<string>();
    }

    public BillingProviderException(string message, Exception innerException, int? providerStatusCode = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        ProviderErrors = Array.Empty<string>();
    }

    /// <summary>HTTP status the billing system responded with, when the call reached it.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>Validation messages returned by the billing system, if any.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }
}
