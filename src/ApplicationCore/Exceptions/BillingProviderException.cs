using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system rejected the call or could not be reached. Distinguishes an upstream problem
/// from a bug in eShopOnWeb so the API surface can answer 4xx vs 502/503 correctly.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(
        string message,
        int? providerStatusCode = null,
        IEnumerable<string>? providerErrors = null,
        bool isTransient = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        ProviderErrors = providerErrors?.ToList() ?? new List<string>();
        IsTransient = isTransient;
    }

    /// <summary>HTTP status the billing system returned, when the call reached it.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>Validation or business messages the billing system returned.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }

    /// <summary>True when retrying the same request later has a reasonable chance of succeeding.</summary>
    public bool IsTransient { get; }
}
