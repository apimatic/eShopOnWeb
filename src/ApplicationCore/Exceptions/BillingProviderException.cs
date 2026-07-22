using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects or cannot serve a request. This is the single
/// application-level failure type the provider seam surfaces, so no caller ever has to know
/// which SDK or transport the concrete billing client uses.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string operation, string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        StatusCode = statusCode;
    }

    /// <summary>The billing operation that failed, for example "CreateSubscription".</summary>
    public string Operation { get; }

    /// <summary>The HTTP status the provider returned, when one was available.</summary>
    public int? StatusCode { get; }
}
