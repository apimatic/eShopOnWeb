using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A stale authorization could not be renewed (e.g. past PayPal's re-authorization window). The
/// fulfilment cannot proceed and an operator must act — the message says so in those terms.
/// </summary>
public class ReauthorizationExpiredException : PaymentGatewayException
{
    public ReauthorizationExpiredException(string message, Exception? innerException = null)
        : base(message, clientStatusCode: 409, innerException)
    {
    }
}
