using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects an operation. This is the single failure type the billing
/// seam surfaces to the rest of eShopOnWeb, so no provider SDK exception ever leaks past Infrastructure.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string operation, string message)
        : base($"The billing provider rejected '{operation}': {message}")
    {
        Operation = operation;
        ProviderMessage = message;
    }

    public BillingProviderException(string operation, string message, int? statusCode)
        : this(operation, message)
    {
        StatusCode = statusCode;
    }

    public BillingProviderException(string operation, string message, int? statusCode, Exception innerException)
        : base($"The billing provider rejected '{operation}': {message}", innerException)
    {
        Operation = operation;
        ProviderMessage = message;
        StatusCode = statusCode;
    }

    /// <summary>The billing seam operation that failed, e.g. <c>CreateSubscription</c>.</summary>
    public string Operation { get; }

    /// <summary>The provider's own message, already extracted from its error envelope.</summary>
    public string ProviderMessage { get; }

    /// <summary>The provider's HTTP status code, when one was reported.</summary>
    public int? StatusCode { get; }
}
