using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the payment provider boundary. The message is always caller-safe
/// (never contains card details or SDK internals).
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? providerStatusCode = null,
        bool isProviderRejection = false,
        string? errorName = null, string? debugId = null, IReadOnlyList<string>? issues = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        IsProviderRejection = isProviderRejection;
        ErrorName = errorName;
        DebugId = debugId;
        Issues = issues ?? Array.Empty<string>();
    }

    /// <summary>The provider's HTTP status code, when known.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>
    /// True when the provider deterministically rejected the request (a typed 4xx error
    /// body) — retrying the identical request can never succeed.
    /// </summary>
    public bool IsProviderRejection { get; }
    public string? ErrorName { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }
}
