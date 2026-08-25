using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentMethodNotFoundException : Exception
{
    public int PaymentMethodId { get; }

    public PaymentMethodNotFoundException(int paymentMethodId) : base($"Payment method {paymentMethodId} was not found.")
    {
        PaymentMethodId = paymentMethodId;
    }
}
