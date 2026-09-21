using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment could not be completed at the provider. <see cref="Message"/> is written to be
/// actionable for an operator (e.g. "authorization can no longer be renewed; create a new
/// payment"). <see cref="IsClientError"/> distinguishes a caller-fixable rejection (bad card,
/// invalid amount) from a provider/transport failure the caller cannot fix.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, bool isClientError = false, string? providerDebugId = null,
        Exception? inner = null)
        : base(message, inner)
    {
        IsClientError = isClientError;
        ProviderDebugId = providerDebugId;
    }

    /// <summary>True when the request was rejected for a reason the caller can correct.</summary>
    public bool IsClientError { get; }

    /// <summary>PayPal's correlation/debug id, when available, for support follow-up.</summary>
    public string? ProviderDebugId { get; }
}
