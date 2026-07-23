using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects or fails an operation. This is the single typed
/// error the provider seam surfaces, so no provider-specific exception ever escapes
/// Infrastructure into ApplicationCore, Web, or PublicApi.
/// </summary>
public class BillingProviderException : Exception
{
    /// <summary>
    /// The HTTP status the provider returned, when the failure came back over the wire.
    /// <see langword="null"/> for transport failures that never reached the provider.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>The provider's own validation messages, when it supplied any.</summary>
    public IReadOnlyCollection<string> ProviderErrors { get; }

    public BillingProviderException(string message)
        : this(message, statusCode: null, providerErrors: null, innerException: null)
    {
    }

    public BillingProviderException(string message, Exception? innerException)
        : this(message, statusCode: null, providerErrors: null, innerException: innerException)
    {
    }

    public BillingProviderException(
        string message,
        int? statusCode,
        IReadOnlyCollection<string>? providerErrors,
        Exception? innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
    }

    /// <summary>
    /// A message safe to render to a customer: the provider's own validation text when it gave
    /// any, otherwise the summary message. Never includes a status line or stack detail.
    /// </summary>
    public string DisplayMessage =>
        ProviderErrors.Any() ? string.Join(" ", ProviderErrors) : Message;
}
