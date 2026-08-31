using System;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// Raised when a call to the Visa invoicing provider fails. Carries whether the failure was the
/// provider legitimately refusing the requested action given the bill's state (for example, a
/// withdrawn or already-paid bill), which the application surfaces to the caller as a conflict
/// rather than an internal error. Never carries secret material.
/// </summary>
public class VisaInvoicingException : Exception
{
    public VisaInvoicingException(string message, bool providerRejected, string? providerReason = null, int? httpStatusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        ProviderRejected = providerRejected;
        ProviderReason = providerReason;
        HttpStatusCode = httpStatusCode;
    }

    /// <summary>True when the provider refused the action because of the state the bill is in.</summary>
    public bool ProviderRejected { get; }

    /// <summary>The provider's machine reason (e.g. ACTION_NOT_ALLOWED), when available.</summary>
    public string? ProviderReason { get; }

    public int? HttpStatusCode { get; }
}
