using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A stale authorization could not be renewed (it is past the window in which PayPal permits reauthorization).
/// The message is phrased for an operator to act on. Surfaces as an HTTP 4xx (the fulfilment cannot proceed and
/// retrying will not help).
/// </summary>
public class PaymentReauthorizationException : PaymentGatewayException
{
    public PaymentReauthorizationException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, statusCode, innerException)
    {
    }
}
