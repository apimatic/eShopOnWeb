using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal rejected the payment itself (declined card, buyer-action contingency such as 3DS).
/// Maps to HTTP 422 with a shopper/operator-actionable message.
/// </summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message)
    {
    }
}
