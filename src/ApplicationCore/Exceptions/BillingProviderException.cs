using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects a call or cannot be reached. Carries the provider's own
/// message and HTTP status so callers can surface something meaningful without knowing which provider
/// is behind the seam.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string operation, string providerMessage, int? statusCode = null, Exception? innerException = null)
        : base(BuildMessage(operation, providerMessage, statusCode), innerException)
    {
        Operation = operation;
        ProviderMessage = providerMessage;
        StatusCode = statusCode;
    }

    /// <summary>The integration operation that failed, e.g. "CreateSubscription".</summary>
    public string Operation { get; }

    /// <summary>The provider's own error text, already extracted from its error payload.</summary>
    public string ProviderMessage { get; }

    /// <summary>The HTTP status the provider returned, when the failure reached the provider at all.</summary>
    public int? StatusCode { get; }

    private static string BuildMessage(string operation, string providerMessage, int? statusCode)
    {
        return statusCode.HasValue
            ? $"Billing provider call '{operation}' failed with status {statusCode.Value}: {providerMessage}"
            : $"Billing provider call '{operation}' failed: {providerMessage}";
    }
}
