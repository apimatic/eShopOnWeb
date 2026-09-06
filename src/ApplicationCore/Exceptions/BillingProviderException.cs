using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider could not be reached, or answered with something we cannot act on.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, int? statusCode = null, IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors?.ToList() ?? new List<string>();
    }

    /// <summary>HTTP status returned by the provider, when the failure came from a response.</summary>
    public int? StatusCode { get; }

    /// <summary>Provider-supplied error messages, safe to relay to the caller.</summary>
    public IReadOnlyList<string> Errors { get; }
}
