using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider refused or could not answer a request. Carries the provider's own
/// status code and messages so the storefront can surface them instead of a generic failure.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
        ProviderErrors = Array.Empty<string>();
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
        ProviderErrors = Array.Empty<string>();
    }

    public BillingProviderException(string operation, int statusCode, IEnumerable<string> providerErrors)
        : base(BuildMessage(operation, statusCode, providerErrors))
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors.ToList();
    }

    /// <summary>The HTTP status the provider answered with, or null when the call never completed.</summary>
    public int? StatusCode { get; }

    /// <summary>The provider's own error messages, in the order it returned them.</summary>
    public IReadOnlyCollection<string> ProviderErrors { get; }

    private static string BuildMessage(string operation, int statusCode, IEnumerable<string> providerErrors)
    {
        var errors = providerErrors.ToList();
        return errors.Any()
            ? $"Billing provider rejected {operation} with status {statusCode}: {string.Join("; ", errors)}"
            : $"Billing provider rejected {operation} with status {statusCode}.";
    }
}
