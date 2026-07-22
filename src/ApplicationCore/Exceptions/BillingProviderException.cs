using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider rejected a request or could not be reached.
/// Thrown only by the billing client, so callers never see transport-level exception types.
/// </summary>
public class BillingProviderException : Exception
{
    private static readonly string[] NoErrors = Array.Empty<string>();

    public BillingProviderException(string message)
        : this(message, null, null, null)
    {
    }

    public BillingProviderException(string message, int? statusCode)
        : this(message, statusCode, null, null)
    {
    }

    public BillingProviderException(string message,
        int? statusCode,
        IEnumerable<string>? providerErrors,
        Exception? innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors?.ToArray() ?? NoErrors;
    }

    /// <summary>The HTTP status the provider responded with, or null when it was unreachable.</summary>
    public int? StatusCode { get; }

    /// <summary>The messages the provider returned, already stripped of any credential material.</summary>
    public IReadOnlyCollection<string> ProviderErrors { get; }
}
