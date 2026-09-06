using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for every failure raised by <see cref="Interfaces.ISubscriptionService"/>.
/// The billing provider's own exception types never escape the infrastructure layer, and the
/// <see cref="Exception.Message"/> carried here is always safe to show to an API caller.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, Exception? innerException = null, int? providerStatusCode = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>
    /// HTTP status the billing provider returned, when one was available. Kept for logs and for
    /// mapping distinct provider failures onto distinct API responses; null for transport
    /// failures, where no status ever existed.
    /// </summary>
    public int? ProviderStatusCode { get; }
}
