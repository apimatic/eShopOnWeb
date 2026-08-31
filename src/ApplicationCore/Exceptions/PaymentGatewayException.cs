using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment processor rejected or failed an operation. The message is safe to show
/// to an operator and never contains card details.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message) : base(message)
    {
    }

    public PaymentGatewayException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public string? PayPalDebugId { get; set; }

    /// <summary>
    /// The HTTP status the processor returned, when known.
    /// </summary>
    public int? ProcessorStatusCode { get; set; }
}
