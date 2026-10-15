using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A billing failure translated into this application's terms. <see cref="Exception.Message"/> is always
/// safe to return to the caller; provider details stay in <see cref="Exception.InnerException"/> and the logs.
/// </summary>
public class BillingException : Exception
{
    public BillingException(BillingErrorKind kind, string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }

    public BillingErrorKind Kind { get; }

    /// <summary>The billing system's HTTP status, when it answered at all.</summary>
    public int? ProviderStatusCode { get; }
}
