using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class BillingException : Exception
{
    public BillingException(string message) : base(message) { }
    public BillingException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class BillingValidationException : BillingException
{
    public BillingValidationException(string message) : base(message) { }
}

public sealed class BillingProviderException : BillingException
{
    public BillingProviderException(string message, HttpStatusCode? providerStatusCode = null)
        : base(message)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public BillingProviderException(string message, HttpStatusCode? providerStatusCode, Exception innerException)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public HttpStatusCode? ProviderStatusCode { get; }
}
