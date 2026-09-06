using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the external billing system rejects a request or is unreachable.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, int? statusCode = null, IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }

    /// <summary>HTTP status returned by the billing system, when the failure came from a response.</summary>
    public int? StatusCode { get; }

    /// <summary>Validation messages reported by the billing system, when it supplied any.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True when the billing system rejected the request as invalid rather than failing to serve it.</summary>
    public bool IsClientError => StatusCode is >= 400 and < 500;
}
