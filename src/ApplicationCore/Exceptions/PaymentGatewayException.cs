using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the payment provider rejects or fails a request. Carries an operator-actionable
/// message plus the provider's debug id and issue codes so a failure can be acted on rather than
/// guessed at. Never carries card details.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(
        string message,
        int? providerStatusCode = null,
        string? debugId = null,
        IReadOnlyList<string>? issues = null,
        bool retryable = false)
        : base(message)
    {
        ProviderStatusCode = providerStatusCode;
        DebugId = debugId;
        Issues = issues ?? Array.Empty<string>();
        Retryable = retryable;
    }

    public int? ProviderStatusCode { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }

    /// <summary>Whether the same request could reasonably be retried (e.g. transient / 5xx / 429).</summary>
    public bool Retryable { get; }
}
