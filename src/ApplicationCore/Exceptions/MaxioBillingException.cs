using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, int? providerStatusCode = null, bool isCallerFault = false, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        IsCallerFault = isCallerFault;
    }

    public int? ProviderStatusCode { get; }
    public bool IsCallerFault { get; }
}
