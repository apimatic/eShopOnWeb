using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects a request or cannot be reached.
/// Thrown only by the <see cref="Interfaces.IBillingClient"/> implementation so that the rest of the
/// application never has to reason about provider transport details.
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

    public BillingProviderException(string message, int statusCode, IEnumerable<string> providerErrors)
        : base(BuildMessage(message, statusCode, providerErrors))
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors.ToList();
    }

    /// <summary>The HTTP status code returned by the provider, when the failure came from a response.</summary>
    public int? StatusCode { get; }

    /// <summary>The messages the provider itself reported, when it supplied any.</summary>
    public IReadOnlyCollection<string> ProviderErrors { get; }

    private static string BuildMessage(string message, int statusCode, IEnumerable<string> providerErrors)
    {
        var details = string.Join("; ", providerErrors);
        return string.IsNullOrEmpty(details)
            ? $"{message} (provider returned {statusCode})"
            : $"{message} (provider returned {statusCode}): {details}";
    }
}
