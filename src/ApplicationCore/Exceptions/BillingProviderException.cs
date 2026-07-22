using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider refused or could not service a request. This is the single typed error the
/// provider seam surfaces, so no provider SDK exception type ever escapes Infrastructure.
/// </summary>
public class BillingProviderException : Exception
{
    /// <summary>Status code reported by the provider, or 0 when the provider could not be reached at all.</summary>
    public const int NoStatusCode = 0;

    public BillingProviderException(string operation, int statusCode, string providerMessage)
        : base($"Billing provider rejected '{operation}'{DescribeStatus(statusCode)}: {providerMessage}")
    {
        Operation = operation;
        StatusCode = statusCode;
        ProviderMessage = providerMessage;
    }

    public BillingProviderException(string operation, int statusCode, string providerMessage, Exception innerException)
        : base($"Billing provider rejected '{operation}'{DescribeStatus(statusCode)}: {providerMessage}", innerException)
    {
        Operation = operation;
        StatusCode = statusCode;
        ProviderMessage = providerMessage;
    }

    /// <summary>The integration operation that failed, e.g. "CreateSubscription".</summary>
    public string Operation { get; }

    /// <summary>The HTTP status the provider returned, or <see cref="NoStatusCode"/> when unreachable.</summary>
    public int StatusCode { get; }

    /// <summary>The provider's own explanation, safe to show to an operator.</summary>
    public string ProviderMessage { get; }

    /// <summary>True when the provider could not be reached, as opposed to having refused the request.</summary>
    public bool IsTransport => StatusCode == NoStatusCode;

    /// <summary>True when the provider reported that the addressed entity does not exist.</summary>
    public bool IsNotFound => StatusCode == 404;

    private static string DescribeStatus(int statusCode) =>
        statusCode == NoStatusCode ? string.Empty : $" (HTTP {statusCode})";
}
