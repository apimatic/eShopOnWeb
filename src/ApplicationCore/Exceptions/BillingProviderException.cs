using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the external billing system rejects a request or cannot be reached. Surfaced to API
/// callers as a bad-gateway response: eShopOnWeb is healthy, its billing dependency is not.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }

    /// <summary>Validation messages reported by the billing system, if any.</summary>
    public IReadOnlyList<string> Errors { get; }
}
