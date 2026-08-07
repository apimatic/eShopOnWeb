using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// How a payment attempt failed, which decides how the API surfaces it to the caller.
/// </summary>
public enum PaymentFailureKind
{
    /// <summary>
    /// The provider rejected the request in a way the caller can act on (declined card,
    /// invalid card data, a business rule such as an already-refunded capture). Caller-actionable.
    /// </summary>
    Rejected,

    /// <summary>
    /// The provider was unreachable, misconfigured, or returned an unusable/unknown response.
    /// Not caller-actionable — a retry may or may not help.
    /// </summary>
    ProviderError
}

/// <summary>
/// A payment-provider failure translated into the application's own type at the integration
/// boundary. Its <see cref="Message"/> is always caller-safe (no provider/SDK type detail),
/// and it never carries card data.
/// </summary>
public class PaymentException : Exception
{
    public PaymentFailureKind Kind { get; }

    public PaymentException(string message, PaymentFailureKind kind, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }
}
