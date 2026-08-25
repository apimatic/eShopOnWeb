using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(
        string message,
        int? providerStatusCode = null,
        bool outcomeMayBeUnknown = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        OutcomeMayBeUnknown = outcomeMayBeUnknown;
    }

    public int? ProviderStatusCode { get; }
    public bool OutcomeMayBeUnknown { get; }
}
