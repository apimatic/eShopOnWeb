using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the payment gateway failed. Carries PayPal's error identity
/// (issue/name and debug id) so operators can act on it; never carries card data.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? issue = null, string? debugId = null,
        int? gatewayStatusCode = null, bool isDecline = false)
        : base(message)
    {
        Issue = issue;
        DebugId = debugId;
        GatewayStatusCode = gatewayStatusCode;
        IsDecline = isDecline;
    }

    /// <summary>PayPal error issue/name, e.g. INSTRUMENT_DECLINED.</summary>
    public string? Issue { get; }
    /// <summary>PayPal debug id for support correlation.</summary>
    public string? DebugId { get; }
    public int? GatewayStatusCode { get; }
    /// <summary>True when the payer's instrument was declined/validation failed (4xx from the gateway).</summary>
    public bool IsDecline { get; }
}
