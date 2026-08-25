using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the payment provider requires the shopper to complete a browser-based approval
/// step (e.g. a 3-D Secure / Strong Customer Authentication challenge) before a card authorization
/// can proceed. This integration is headless end-to-end and does not implement that round-trip;
/// this exception surfaces the situation to the caller/operator instead of silently retrying.
/// </summary>
public class PaymentActionRequiredException : Exception
{
    public PaymentActionRequiredException(string message) : base(message)
    {
    }
}
