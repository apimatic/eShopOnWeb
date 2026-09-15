using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A business-rule violation in the payment flow that an operator or shopper can act on
/// (e.g. paying an already-cancelled order, refunding beyond what was captured, or an
/// authorization that can no longer be renewed). Distinct from <see cref="PayPalApiException"/>,
/// which wraps raw PayPal API failures.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }

    public PaymentException(string message, Exception innerException) : base(message, innerException) { }
}
