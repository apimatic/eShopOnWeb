using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the PayPal gateway surfaces to the rest of the app. It carries the
/// transport status (where the provider answered), the provider's correlation/debug id (for our
/// logs), whether the outcome is <em>unknown</em> (a transport failure after which the write may
/// still have landed), and whether the condition is one an operator must act on (e.g. an
/// authorization that can no longer be renewed).
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? statusCode = null,
        string? providerDebugId = null, bool outcomeUnknown = false,
        bool operatorActionable = false, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderDebugId = providerDebugId;
        OutcomeUnknown = outcomeUnknown;
        OperatorActionable = operatorActionable;
    }

    /// <summary>HTTP status PayPal returned, when it answered at all.</summary>
    public int? StatusCode { get; }

    /// <summary>PayPal's debug/correlation id from the error body, for support and log correlation.</summary>
    public string? ProviderDebugId { get; }

    /// <summary>True when a write may have taken effect despite the failure (connection/timeout).</summary>
    public bool OutcomeUnknown { get; }

    /// <summary>True when the message is phrased for an operator to act on (e.g. re-collect payment).</summary>
    public bool OperatorActionable { get; }
}
