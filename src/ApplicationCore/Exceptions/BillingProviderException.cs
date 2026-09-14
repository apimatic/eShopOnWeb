using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider could not be reached, or answered in a way this application cannot use.
/// These are upstream faults: the caller's request was well formed.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, int? statusCode = null,
        IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors?.ToList() ?? new List<string>();
    }

    /// <summary>HTTP status code returned by the billing provider, when there was a response.</summary>
    public int? StatusCode { get; }

    /// <summary>Error messages reported by the billing provider.</summary>
    public IReadOnlyList<string> Errors { get; }
}
