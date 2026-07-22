using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects an operation or cannot be reached. This is the
/// single typed error the provider seam surfaces, so callers never see transport-level types.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
        ProviderErrors = Array.Empty<string>();
    }

    public BillingProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
        ProviderErrors = Array.Empty<string>();
    }

    public BillingProviderException(string message, int? statusCode, IEnumerable<string>? providerErrors)
        : base(BuildMessage(message, providerErrors))
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors?.ToArray() ?? Array.Empty<string>();
    }

    /// <summary>The HTTP status the provider responded with, when the call reached it.</summary>
    public int? StatusCode { get; }

    /// <summary>The provider's own error messages, in the order it returned them.</summary>
    public IReadOnlyCollection<string> ProviderErrors { get; }

    private static string BuildMessage(string message, IEnumerable<string>? providerErrors)
    {
        var errors = providerErrors?.ToArray() ?? Array.Empty<string>();

        return errors.Any() ? $"{message}: {string.Join("; ", errors)}" : message;
    }
}
